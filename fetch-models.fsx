#!/usr/bin/env dotnet fsi
#load "models.fsx"

open System
open System.IO
open System.Net.Http
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Threading.Tasks

// JSON serialization options
let jsonOptions = JsonSerializerOptions()
jsonOptions.PropertyNameCaseInsensitive <- true
jsonOptions.WriteIndented <- true
jsonOptions.ReadCommentHandling <- JsonCommentHandling.Skip
jsonOptions.AllowTrailingCommas <- true
jsonOptions.Encoder <- JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 避免中文字符被转义为\uXXXX

let docOptions =
    JsonDocumentOptions(CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)

let nodeOptions = JsonNodeOptions()
// Data models for configuration
[<CLIMutable>]
type EndpointConfig =
    { key: string
      name: string
      npm: string
      baseUrl: string
      apiKey: string
      whitelist: string[]
      blacklist: string[] }

[<CLIMutable>]
type EnvConfig = { endpoints: EndpointConfig[] }

// Data models for API
[<CLIMutable>]
type ModelData =
    { id: string
      ``object``: string
      created: int64
      owned_by: string }

[<CLIMutable>]
type ModelsResponse =
    { ``object``: string
      data: ModelData[] }

// Load configuration from env.json
let loadConfig () =
    let configPath = Path.Combine(__SOURCE_DIRECTORY__, "env.json")

    if not (File.Exists configPath) then
        printfn "Error: env.json not found. Please create it based on env.json.example"
        exit 1

    let jsonContent = File.ReadAllText configPath
    JsonSerializer.Deserialize<EnvConfig>(jsonContent, jsonOptions)

// Filter models based on whitelist and blacklist
let filterModels (models: ModelData[]) (whitelist: string[]) (blacklist: string[]) =
    let matchesPattern (pattern: string) (text: string) =
        try
            Regex.IsMatch(text, pattern)
        with ex ->
            printfn "Warning: Invalid regex pattern '%s': %s" pattern ex.Message
            false

    let matchesAnyPattern (patterns: string[]) (text: string) =
        patterns |> Array.exists (fun pattern -> matchesPattern pattern text)

    models
    |> Array.filter (fun model ->
        // If whitelist is not empty, model must match at least one whitelist pattern
        let passesWhitelist =
            if whitelist.Length = 0 then
                true
            else
                matchesAnyPattern whitelist model.id

        // Model must not match any blacklist pattern
        let passesBlacklist =
            if blacklist.Length = 0 then
                true
            else
                not (matchesAnyPattern blacklist model.id)

        passesWhitelist && passesBlacklist)

// Get user directory dynamically using environment variables for better compatibility
let getUserConfigPath () =
    let userProfile =
        match Environment.GetEnvironmentVariable "USERPROFILE" with
        | null
        | "" ->
            // Fallback to HOME for Unix-like systems
            match Environment.GetEnvironmentVariable "HOME" with
            | null
            | "" -> Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            | home -> home
        | profile -> profile

    let configDir = Path.Combine(userProfile, ".config", "opencode")

    // Try to find config file with either .jsonc or .json extension
    let jsoncPath = Path.Combine(configDir, "opencode.jsonc")
    let jsonPath = Path.Combine(configDir, "opencode.json")

    if File.Exists jsoncPath then
        jsoncPath
    elif File.Exists jsonPath then
        jsonPath
    else
        // Default to .jsonc if neither exists
        jsoncPath

// Configuration paths
let oldConfigPath = getUserConfigPath ()
let newConfigPath = @"opencode.jsonc"

// Download models.dev API JSON and save to models.json
let downloadModelsJson () =
    task {
        let url = "https://models.dev/api.json"
        let outputPath = Path.Combine(__SOURCE_DIRECTORY__, "models.json")

        // 检查文件是否存在且修改时间不足 1 天
        if File.Exists outputPath then
            let fileAge = DateTime.Now - File.GetLastWriteTime outputPath

            if fileAge.TotalHours < 24.0 then
                printfn "models.json 最后更新于 %.1f 小时前，跳过下载 (将在 %.1f 小时后更新)" fileAge.TotalHours (24.0 - fileAge.TotalHours)
                return ()

        printfn "Downloading models.dev API JSON..."

        use client = new HttpClient(Timeout = TimeSpan.FromSeconds 60.0)

        try
            let! response = client.GetAsync url
            response.EnsureSuccessStatusCode() |> ignore

            let! content = response.Content.ReadAsStringAsync()

            // Parse and re-serialize with indentation
            let jsonNode = JsonNode.Parse(content, nodeOptions)
            let formattedJson = jsonNode.ToJsonString jsonOptions

            File.WriteAllText(outputPath, formattedJson)
            printfn "Saved models.json (%d bytes)" formattedJson.Length
        with ex ->
            printfn "Warning: Failed to download models.json: %s" ex.Message
    }

// Fetch models from API
let fetchModels (baseUrl: string) (apiKey: string) =
    task {
        use client = new HttpClient()
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}")

        let! response = client.GetAsync $"{baseUrl}/models"
        response.EnsureSuccessStatusCode() |> ignore

        let! content = response.Content.ReadAsStringAsync()
        return content
    }

let requireFixContextOverflow (data: ModelData) (info: Models.ModelInfo) =
    match info with
    | _ when info.name.Contains("gemini", StringComparison.OrdinalIgnoreCase) -> true
    | _ -> false

let normalizeModelId (modelId: string) =
    let modelId =
        match modelId with
        | _ when modelId.Contains "/" -> modelId.Substring(modelId.LastIndexOf '/' + 1)
        | _ -> modelId

    match modelId with
    | "copilot/raptor-mini" -> "gpt-5-mini"
    | "kiro-auto" -> "claude-sonnet-4-6"
    | _ when modelId.EndsWith "-low" || modelId.EndsWith "-high" -> modelId.Substring(0, modelId.LastIndexOf '-')
    | _ when modelId.StartsWith "gemini-claude" -> modelId.Substring("gemini-".Length)
    | _ when modelId.StartsWith "kiro" -> modelId.Substring("kiro-".Length)
    | _ when modelId.EndsWith "-low" || modelId.EndsWith "-high" -> modelId.Substring(0, modelId.LastIndexOf '-')
    | _ -> modelId

let setOptionalString (node: JsonObject) (key: string) (value: string option) =
    match value with
    | Some v -> node.[key] <- v
    | None -> ()

let setOptionalStringList (node: JsonObject) (key: string) (value: string list option) =
    match value with
    | Some values ->
        let items = JsonArray()
        values |> List.iter items.Add
        node.[key] <- items
    | None -> ()

let buildModelOptionsNode (options: Models.ModelOptions) =
    let optionsNode = JsonObject()

    match options.thinking with
    | Some thinking ->
        let thinkingNode = JsonObject()
        thinkingNode.["type"] <- thinking.``type``

        match thinking.budgetTokens with
        | Some budgetTokens -> thinkingNode.["budgetTokens"] <- budgetTokens
        | None -> ()

        optionsNode.["thinking"] <- thinkingNode
    | None -> ()

    setOptionalString optionsNode "reasoningEffort" options.reasoningEffort
    setOptionalString optionsNode "textVerbosity" options.textVerbosity
    setOptionalString optionsNode "reasoningSummary" options.reasoningSummary
    setOptionalStringList optionsNode "include" options.``include``
    optionsNode

let buildVariantsNode (variants: Models.ModelVariant list) =
    let variantsNode = JsonObject()

    variants
    |> List.iter (fun variant -> variantsNode.[variant.name] <- buildModelOptionsNode variant.options)

    variantsNode
// Replace providers section in config using JsonNode
let replaceProvidersInConfig (configContent: string) (endpoints: (EndpointConfig * ModelData[]) list) =
    try
        // Parse the original config
        let configNode = JsonNode.Parse(configContent, JsonNodeOptions(), docOptions)

        // Create new providers object
        let providersNode = JsonObject()

        for endpoint, models in endpoints do
            let providerKey = endpoint.key
            let providerNode = JsonObject()

            // Add name
            providerNode.["name"] <- endpoint.name
            providerNode.["npm"] <- endpoint.npm

            // Add options
            let optionsNode = JsonObject()

            if endpoint.npm = "@ai-sdk/open-responses" then
                optionsNode.["url"] <- endpoint.baseUrl + "/v1/responses"
            else
                optionsNode.["baseURL"] <- endpoint.baseUrl + "/v1"

            optionsNode.["apiKey"] <- endpoint.apiKey
            providerNode.["options"] <- optionsNode

            // Add models
            let modelsNode = JsonObject()

            for model in models do
                let mutable modelNode = JsonObject()

                match Models.queryModel (normalizeModelId model.id) 0.75 with
                | Some info ->
                    modelNode <- JsonNode.Parse(info.raw.GetRawText(), JsonNodeOptions(), docOptions).AsObject()
                    modelNode.["id"] <- model.id

                    if
                        String.Equals(
                            info.name.Replace("-", " "),
                            model.id.Replace("-", " "),
                            StringComparison.OrdinalIgnoreCase
                        )
                    then
                        modelNode["name"] <- info.name
                    else
                        modelNode["name"] <- $"{info.name} ({model.id})"

                    modelNode.Remove "provider" |> ignore
                    modelNode.Remove "experimental" |> ignore

                    if requireFixContextOverflow model info then
                        let originalCtx = modelNode.["limit"].["context"].AsValue().GetValue<int>()
                        let originalOutput = modelNode.["limit"].["output"].AsValue().GetValue<int>()

                        printfn
                            "Original context: %d, output: %d for model %s (overflow: %b)"
                            originalCtx
                            originalOutput
                            model.id
                            (originalCtx > 2 * originalOutput)

                        if originalCtx > 2 * originalOutput then
                            modelNode["limit"]["context"] <- originalOutput * 2

                    match Models.getReasoningParams info with
                    | Some rp ->
                        match rp.options with
                        | Some options ->
                            let optionsNode = buildModelOptionsNode options

                            if optionsNode.Count > 0 then
                                modelNode.["options"] <- optionsNode
                        | _ -> ()

                        match rp.variants with
                        | variants when not variants.IsEmpty -> modelNode.["variants"] <- buildVariantsNode variants
                        | _ -> ()
                    | None -> ()
                | None -> modelNode.["name"] <- model.id
                    // 生成 variant

                modelsNode.[model.id] <- modelNode

            providerNode.["models"] <- modelsNode

            providersNode.[providerKey] <- providerNode

        // Replace provider section in config
        configNode.AsObject().["provider"] <- providersNode

        // Serialize back to string
        configNode.ToJsonString jsonOptions
    with ex ->
        printfn "Error: Failed to process config: %s" ex.Message
        configContent

// Ask for user confirmation with default No
let askConfirmation (message: string) (defaultYes: bool) =
    let defaultStr = if defaultYes then "(Y/n)" else "(y/N)"
    printf "%s %s " message defaultStr
    let input = Console.ReadLine()

    let normalized =
        if String.IsNullOrWhiteSpace(input) then
            ""
        else
            input.Trim().ToLower()

    if defaultYes then
        normalized = "" || normalized = "y" || normalized = "yes"
    else
        normalized = "y" || normalized = "yes"

// Main function
let main () =
    task {
        // Download latest models.json from models.dev
        do! downloadModelsJson ()

        // Collect models from all endpoints
        let mutable endpointModels = []

        try
            printfn "Loading configuration from env.json..."
            let config = loadConfig ()

            if config.endpoints.Length = 0 then
                printfn "Error: No endpoints configured in env.json"
                Environment.Exit 1

            printfn "Found %d endpoint(s) configured\n" config.endpoints.Length

            for endpoint in config.endpoints do
                printfn "=========================================="
                printfn "Processing endpoint: %s" endpoint.name
                printfn "URL: %s" endpoint.baseUrl
                printfn "=========================================="

                if String.IsNullOrWhiteSpace(endpoint.apiKey) then
                    printfn "Warning: API key is empty for endpoint '%s', skipping..." endpoint.name
                    printfn ""
                else
                    try
                        printfn "Fetching models from %s/models..." endpoint.baseUrl
                        let! jsonContent = fetchModels (endpoint.baseUrl + "/v1") endpoint.apiKey
                        let response = JsonSerializer.Deserialize<ModelsResponse>(jsonContent, jsonOptions)

                        printfn "Found %d models before filtering" response.data.Length

                        // Apply filters
                        let filteredModels =
                            filterModels response.data endpoint.whitelist endpoint.blacklist

                        printfn "After filtering: %d models" filteredModels.Length

                        if endpoint.whitelist.Length > 0 then
                            printfn "  Whitelist patterns: %s" (String.Join(", ", endpoint.whitelist))

                        if endpoint.blacklist.Length > 0 then
                            printfn "  Blacklist patterns: %s" (String.Join(", ", endpoint.blacklist))

                        printfn "\nFiltered models from %s:" endpoint.name

                        filteredModels
                        |> Array.iter (fun model -> printfn "  - %s (owned by: %s)" model.id model.owned_by)

                        endpointModels <- endpointModels @ [ (endpoint, filteredModels) ]
                        printfn ""
                    with ex ->
                        printfn "Error fetching models from %s: %s" endpoint.name ex.Message
                        printfn ""

        with ex ->
            printfn "Error: %s" ex.Message
            Environment.Exit 1

        if endpointModels.Length = 0 then
            printfn "Error: No models found from any endpoint"
            Environment.Exit 1
        else
            let totalModels = endpointModels |> List.sumBy (fun (_, models) -> models.Length)
            printfn "Total models collected: %d" totalModels
            printfn "=========================================="

            printfn "\nReading old config from: %s" oldConfigPath

            if not (File.Exists oldConfigPath) then
                printfn "Error: Config file not found at %s" oldConfigPath
                Environment.Exit 1
            else
                let oldConfig = File.ReadAllText oldConfigPath
                printfn "Generating new providers configuration..."
                let newConfig = replaceProvidersInConfig oldConfig endpointModels

                printfn "Writing new config to: %s" newConfigPath
                File.WriteAllText(newConfigPath, newConfig)

                printfn "\n=========================================="
                printfn "SUCCESS!"
                printfn "=========================================="
                printfn "New config file created: %s" (Path.GetFullPath(newConfigPath))
                printfn "\nGenerated %d provider(s):" endpointModels.Length

                for endpoint, models in endpointModels do
                    let providerKey = endpoint.key
                    printfn "  - %s (%d models)" providerKey models.Length

                printfn "\nDo you want to replace the existing config file at:"
                printfn "  %s" oldConfigPath
                let overwrite = askConfirmation "替换文件?" false

                if overwrite then
                    try
                        File.Copy(newConfigPath, oldConfigPath, true)
                        printfn "Successfully replaced %s" oldConfigPath
                    with ex ->
                        printfn "Failed to replace config file: %s" ex.Message
                        printfn "Please manually copy the file:"
                        printfn "  copy /Y opencode.jsonc \"%s\"" oldConfigPath
                else
                    printfn "Config file not replaced."
                    printfn "\nPlease manually copy this file to:"
                    printfn "  %s" oldConfigPath
                    printfn "\nOr run the following command:"
                    printfn "  copy /Y opencode.jsonc \"%s\"" oldConfigPath
                    printfn "=========================================="
    }

// Run
main().Result |> ignore
