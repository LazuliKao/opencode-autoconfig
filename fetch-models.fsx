#!/usr/bin/env dotnet fsi

open System
open System.IO
open System.Net.Http
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Threading.Tasks

// JSON serialization options
let jsonOptions = JsonSerializerOptions()
jsonOptions.PropertyNameCaseInsensitive <- true
jsonOptions.WriteIndented <- true

// Data models for configuration
[<CLIMutable>]
type EndpointConfig = {
    name: string
    baseUrl: string
    apiKey: string
    whitelist: string[]
    blacklist: string[]
}

[<CLIMutable>]
type EnvConfig = {
    endpoints: EndpointConfig[]
}

// Data models for API
[<CLIMutable>]
type ModelData = {
    id: string
    ``object``: string
    created: int64
    owned_by: string
}

[<CLIMutable>]
type ModelsResponse = {
    ``object``: string
    data: ModelData[]
}

// Load configuration from env.json
let loadConfig () =
    let configPath = Path.Combine(__SOURCE_DIRECTORY__, "env.json")
    if not (File.Exists(configPath)) then
        printfn "Error: env.json not found. Please create it based on env.json.example"
        exit 1
    
    let jsonContent = File.ReadAllText(configPath)
    JsonSerializer.Deserialize<EnvConfig>(jsonContent, jsonOptions)

// Filter models based on whitelist and blacklist
let filterModels (models: ModelData[]) (whitelist: string[]) (blacklist: string[]) =
    let matchesPattern (pattern: string) (text: string) =
        try
            Regex.IsMatch(text, pattern)
        with
        | ex ->
            printfn "Warning: Invalid regex pattern '%s': %s" pattern ex.Message
            false
    
    let matchesAnyPattern (patterns: string[]) (text: string) =
        patterns |> Array.exists (fun pattern -> matchesPattern pattern text)
    
    models
    |> Array.filter (fun model ->
        // If whitelist is not empty, model must match at least one whitelist pattern
        let passesWhitelist = 
            if whitelist.Length = 0 then true
            else matchesAnyPattern whitelist model.id
        
        // Model must not match any blacklist pattern
        let passesBlacklist = 
            if blacklist.Length = 0 then true
            else not (matchesAnyPattern blacklist model.id)
        
        passesWhitelist && passesBlacklist
    )

// Configuration paths
let oldConfigPath = @"C:\Users\lk\.config\opencode\opencode.jsonc"
let newConfigPath = @"opencode.jsonc"

// Fetch models from API
let fetchModels (baseUrl: string) (apiKey: string) =
    task {
        use client = new HttpClient()
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}")
        
        let! response = client.GetAsync($"{baseUrl}/models")
        response.EnsureSuccessStatusCode() |> ignore
        
        let! content = response.Content.ReadAsStringAsync()
        return content
    }

// Generate provider key from endpoint name
let generateProviderKey (name: string) =
    name.ToLowerInvariant()
        .Replace(" ", "-")
        .Replace("的", "")
        .Replace("中转站", "")
        .Trim('-')

// Replace providers section in config using JsonNode
let replaceProvidersInConfig (configContent: string) (endpoints: (EndpointConfig * ModelData[]) list) =
    try
        // Parse the original config
        let configNode = JsonNode.Parse(configContent)
        
        // Create new providers object
        let providersNode = JsonObject()
        
        for (endpoint, models) in endpoints do
            let providerKey = generateProviderKey endpoint.name
            let providerNode = JsonObject()
            
            // Add name
            providerNode.["name"] <- JsonValue.Create(endpoint.name)
            
            // Add options
            let optionsNode = JsonObject()
            optionsNode.["baseURL"] <- JsonValue.Create(endpoint.baseUrl)
            optionsNode.["apiKey"] <- JsonValue.Create(endpoint.apiKey)
            providerNode.["options"] <- optionsNode
            
            // Add models
            let modelsNode = JsonObject()
            for model in models do
                let modelNode = JsonObject()
                modelNode.["name"] <- JsonValue.Create(model.id)
                modelsNode.[model.id] <- modelNode
            providerNode.["models"] <- modelsNode
            
            providersNode.[providerKey] <- providerNode
        
        // Replace provider section in config
        configNode.AsObject().["provider"] <- providersNode
        
        // Serialize back to string
        configNode.ToJsonString(jsonOptions)
    with
    | ex ->
        printfn "Error: Failed to process config: %s" ex.Message
        configContent

// Main function
let main () =
    task {
        try
            printfn "Loading configuration from env.json..."
            let config = loadConfig()
            
            if config.endpoints.Length = 0 then
                printfn "Error: No endpoints configured in env.json"
                return 1
            else
                printfn "Found %d endpoint(s) configured\n" config.endpoints.Length
                
                // Collect models from all endpoints
                let mutable endpointModels = []
                
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
                            let! jsonContent = fetchModels endpoint.baseUrl endpoint.apiKey
                            let response = JsonSerializer.Deserialize<ModelsResponse>(jsonContent, jsonOptions)
                            
                            printfn "Found %d models before filtering" response.data.Length
                            
                            // Apply filters
                            let filteredModels = filterModels response.data endpoint.whitelist endpoint.blacklist
                            
                            printfn "After filtering: %d models" filteredModels.Length
                            
                            if endpoint.whitelist.Length > 0 then
                                printfn "  Whitelist patterns: %s" (String.Join(", ", endpoint.whitelist))
                            
                            if endpoint.blacklist.Length > 0 then
                                printfn "  Blacklist patterns: %s" (String.Join(", ", endpoint.blacklist))
                            
                            printfn "\nFiltered models from %s:" endpoint.name
                            filteredModels
                            |> Array.iter (fun model ->
                                printfn "  - %s (owned by: %s)" model.id model.owned_by
                            )
                            
                            endpointModels <- endpointModels @ [(endpoint, filteredModels)]
                            printfn ""
                        with
                        | ex ->
                            printfn "Error fetching models from %s: %s" endpoint.name ex.Message
                            printfn ""
                
                if endpointModels.Length = 0 then
                    printfn "Error: No models found from any endpoint"
                    return 1
                else
                    let totalModels = endpointModels |> List.sumBy (fun (_, models) -> models.Length)
                    printfn "=========================================="
                    printfn "Total models collected: %d" totalModels
                    printfn "=========================================="
                    
                    printfn "\nReading old config from: %s" oldConfigPath
                    
                    if not (File.Exists(oldConfigPath)) then
                        printfn "Error: Config file not found at %s" oldConfigPath
                        return 1
                    else
                        let oldConfig = File.ReadAllText(oldConfigPath)
                        
                        printfn "Generating new providers configuration..."
                        let newConfig = replaceProvidersInConfig oldConfig endpointModels
                        
                        printfn "Writing new config to: %s" newConfigPath
                        File.WriteAllText(newConfigPath, newConfig)
                        
                        printfn "\n=========================================="
                        printfn "SUCCESS!"
                        printfn "=========================================="
                        printfn "New config file created: %s" (Path.GetFullPath(newConfigPath))
                        printfn "\nGenerated %d provider(s):" endpointModels.Length
                        for (endpoint, models) in endpointModels do
                            let providerKey = generateProviderKey endpoint.name
                            printfn "  - %s (%d models)" providerKey models.Length
                        printfn "\nPlease manually copy this file to:"
                        printfn "  %s" oldConfigPath
                        printfn "\nOr run the following command:"
                        printfn "  copy /Y opencode.jsonc \"%s\"" oldConfigPath
                        printfn "=========================================="
                        
                        return 0
        with
        | ex ->
            printfn "Error: %s" ex.Message
            printfn "Stack trace: %s" ex.StackTrace
            return 1
    }

// Run
main().Result |> exit
