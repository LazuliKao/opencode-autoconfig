open System
open System.Text.Json
open System.IO

// 类型定义
type Cost = { input: float; output: float }

type Limit = { context: int; output: int }

type Modalities =
    { input: string list
      output: string list }

type ModelInfo =
    { raw: JsonElement
      id: string
      name: string
      family: string
      cost: Cost
      limit: Limit
      modalities: Modalities
      reasoning: bool
      tool_call: bool
      open_weights: bool
      release_date: string option }

// 推理参数类型
type ThinkingParams =
    { ``type``: string
      budgetTokens: int option }

type ModelOptions =
    { thinking: ThinkingParams option
      reasoningEffort: string option
      textVerbosity: string option
      reasoningSummary: string option
      ``include``: string list option }

type ModelVariant = { name: string; options: ModelOptions }

type ModelConfig =
    { options: ModelOptions option
      variants: ModelVariant list }

type VariantParams = ModelConfig option

/// <summary>获取模型的推理参数</summary>
/// <param name="model">模型</param>
/// <returns></returns>
let getReasoningParams (model: ModelInfo) : VariantParams =
    let id = model.id.ToLowerInvariant()
    let name = model.name.ToLowerInvariant()
    let family = model.family.ToLowerInvariant()
    let (!=) (m: string) = id = m
    let (!<) (m: string) = id.Contains m
    let (!@) (f: string) = f = family
    let (!@<) (f: string) = family.Contains f

    let gptNone =
        { name = "none"
          options =
            { thinking = None
              reasoningEffort = None
              textVerbosity = None
              reasoningSummary = None
              ``include`` = None } }

    let gptLow =
        { name = "low"
          options =
            { thinking = None
              reasoningEffort = Some "low"
              textVerbosity = Some "low"
              reasoningSummary = Some "auto"
              ``include`` = None } }

    let gptMinimal =
        { name = "minimal"
          options =
            { thinking = None
              reasoningEffort = Some "minimal"
              textVerbosity = Some "low"
              reasoningSummary = Some "auto"
              ``include`` = None } }

    let gptMedium =
        { name = "medium"
          options =
            { thinking = None
              reasoningEffort = Some "medium"
              textVerbosity = Some "low"
              reasoningSummary = Some "auto"
              ``include`` = None } }

    let gptHigh =
        { name = "high"
          options =
            { thinking = None
              reasoningEffort = Some "high"
              textVerbosity = Some "low"
              reasoningSummary = Some "auto"
              ``include`` = None } }

    let gptXHigh =
        { name = "xhigh"
          options =
            { thinking = None
              reasoningEffort = Some "xhigh"
              textVerbosity = Some "low"
              reasoningSummary = Some "auto"
              ``include`` = None } }

    let thinkingNone =
        { name = "none"
          options =
            { thinking =
                Some
                    { ``type`` = "disabled"
                      budgetTokens = None }
              reasoningEffort = None
              textVerbosity = None
              reasoningSummary = None
              ``include`` = None } }

    let thinkingLow =
        { name = "low"
          options =
            { thinking =
                Some
                    { ``type`` = "enabled"
                      budgetTokens = Some 8000 }
              reasoningEffort = None
              textVerbosity = None
              reasoningSummary = None
              ``include`` = None } }

    let thinkingHigh =
        { name = "high"
          options =
            { thinking =
                Some
                    { ``type`` = "enabled"
                      budgetTokens = Some 16000 }
              reasoningEffort = None
              textVerbosity = None
              reasoningSummary = None
              ``include`` = None } }

    match model with
    | _ when family.Contains "claude" && (!<"4" || !<"sonnet" || !<"opus") ->
        Some
            { options =
                Some
                    { thinking =
                        Some
                            { ``type`` = "enabled"
                              budgetTokens = Some 16000 }
                      reasoningEffort = None
                      textVerbosity = None
                      reasoningSummary = None
                      ``include`` = None }
              variants = [] }

    | _ when !<"gpt-5" && (!<"gpt-5.3-codex" || !<"gpt-5.4") ->
        Some
            { options =
                Some
                    { thinking = None
                      reasoningEffort = gptMedium.options.reasoningEffort
                      textVerbosity = None
                      reasoningSummary = None
                      ``include`` = None }
              variants = [ gptNone; gptMinimal; gptLow; gptMedium; gptHigh; gptXHigh ] }
    | _ when !<"gpt-5" ->
        Some
            { options =
                Some
                    { thinking = None
                      reasoningEffort = Some "high"
                      textVerbosity = Some "low"
                      reasoningSummary = Some "auto"
                      ``include`` = None }
              variants = [] }
    | _ when !<"minimax-m2" ->
        Some
            { options = None
              variants = [ thinkingNone; thinkingLow; thinkingHigh ] }

    | _ -> None
// if family.Contains "claude" && (!< "4" || !< "sonnet" || !< "opus") then
//     Some { reasoningEffort = "high"; textVerbosity = "low"; reasoningSummary = "auto" }
// elif !< "gemini" && (model.reasoning || name.Contains "thinking") then
//     Some { reasoningEffort = "high"; textVerbosity = "low"; reasoningSummary = "auto" }
// elif family.Contains "qwen" && (!< "thinking" || !< "a3b") then
//     Some { reasoningEffort = "high"; textVerbosity = "low"; reasoningSummary = "auto" }
// elif !< "gpt-4o" || !< "gpt-5" then
//     Some { reasoningEffort = "medium"; textVerbosity = "low"; reasoningSummary = "auto" }
// else
//     None

type ProviderModels =
    { id: string
      name: string
      models: ModelInfo list }

// JSON 反序列化模块
module JsonParser =
    let parseJsonElement (element: JsonElement) (key: string) : JsonElement option =
        let exists, value = element.TryGetProperty key
        if exists then Some value else None

    let getStringValue (element: JsonElement) (key: string) : string option =
        match parseJsonElement element key with
        | Some el ->
            match el.ValueKind with
            | JsonValueKind.String -> Some(el.GetString())
            | _ -> None
        | None -> None

    let getFloatValue (element: JsonElement) (key: string) : float option =
        match parseJsonElement element key with
        | Some el ->
            match el.ValueKind with
            | JsonValueKind.Number -> Some(el.GetDouble())
            | _ -> None
        | None -> None

    let getIntValue (element: JsonElement) (key: string) : int option =
        match parseJsonElement element key with
        | Some el ->
            match el.ValueKind with
            | JsonValueKind.Number -> Some(el.GetInt32())
            | _ -> None
        | None -> None

    let getBoolValue (element: JsonElement) (key: string) : bool =
        match parseJsonElement element key with
        | Some el ->
            match el.ValueKind with
            | JsonValueKind.True -> true
            | JsonValueKind.False -> false
            | _ -> false
        | None -> false

    let getStringListValue (element: JsonElement) (key: string) : string list =
        match parseJsonElement element key with
        | Some el ->
            match el.ValueKind with
            | JsonValueKind.Array ->
                el.EnumerateArray()
                |> Seq.choose (fun e ->
                    if e.ValueKind = JsonValueKind.String then
                        Some(e.GetString())
                    else
                        None)
                |> List.ofSeq
            | _ -> []
        | None -> []

    let parseCost (element: JsonElement) : Cost option =
        match parseJsonElement element "cost" with
        | Some costEl ->
            match (getFloatValue costEl "input", getFloatValue costEl "output") with
            | (Some input, Some output) -> Some { input = input; output = output }
            | _ -> None
        | None -> None

    let parseLimit (element: JsonElement) : Limit option =
        match parseJsonElement element "limit" with
        | Some limitEl ->
            match (getIntValue limitEl "context", getIntValue limitEl "output") with
            | (Some context, Some output) -> Some { context = context; output = output }
            | _ -> None
        | None -> None

    let parseModalities (element: JsonElement) : Modalities option =
        match parseJsonElement element "modalities" with
        | Some modalEl ->
            let input = getStringListValue modalEl "input"
            let output = getStringListValue modalEl "output"
            Some { input = input; output = output }
        | None -> None

    let getStatus (element: JsonElement) (key: string) : bool =
        match parseJsonElement element key with
        | Some el ->
            match el.ValueKind with
            | JsonValueKind.String ->
                match el.GetString() with
                | "deprecated" -> false
                | _ -> true
            | _ -> true
        | None -> true

    let parseModel (id: string) (element: JsonElement) : ModelInfo option =
        match
            getStatus element "status",
            getStringValue element "name",
            getStringValue element "family",
            parseCost element,
            parseLimit element,
            parseModalities element
        with
        | true, Some name, Some family, Some cost, Some limit, Some modalities ->
            let reasoning = getBoolValue element "reasoning"
            let tool_call = getBoolValue element "tool_call"
            let open_weights = getBoolValue element "open_weights"
            let release_date = getStringValue element "release_date"

            Some
                { raw = element
                  id = id
                  name = name
                  family = family
                  cost = cost
                  limit = limit
                  modalities = modalities
                  reasoning = reasoning
                  tool_call = tool_call
                  open_weights = open_weights
                  release_date = release_date }
        | _ -> None

    let parseProvider (providerId: string) (element: JsonElement) : ProviderModels option =
        match (getStringValue element "name") with
        | (Some name) ->
            let models =
                match parseJsonElement element "models" with
                | Some modelsEl ->
                    if modelsEl.ValueKind = JsonValueKind.Object then
                        modelsEl.EnumerateObject()
                        |> Seq.choose (fun prop -> parseModel prop.Name prop.Value)
                        |> List.ofSeq
                    else
                        []
                | None -> []

            Some
                { id = providerId
                  name = name
                  models = models }
        | _ -> None

    let filterModels (prop: JsonProperty) : bool =
        match prop.Name with
        | "302ai" -> false
        | _ -> true

    let parseModelsJson (jsonPath: string) : ProviderModels list =
        try
            let jsonText = File.ReadAllText jsonPath
            let doc = JsonDocument.Parse jsonText
            let root = doc.RootElement

            root.EnumerateObject()
            |> Seq.filter filterModels
            |> Seq.choose (fun prop -> parseProvider prop.Name prop.Value)
            |> List.ofSeq
        with ex ->
            eprintfn "Error parsing models.json: %s" ex.Message
            []

// 模糊匹配模块
module FuzzyMatch =
    let levenshteinDistance (s1: string) (s2: string) : int =
        let s1 = s1.ToLowerInvariant()
        let s2 = s2.ToLowerInvariant()
        let len1 = String.length s1
        let len2 = String.length s2

        let d = Array2D.create (len1 + 1) (len2 + 1) 0

        for i = 0 to len1 do
            d.[i, 0] <- i

        for j = 0 to len2 do
            d.[0, j] <- j

        for i = 1 to len1 do
            for j = 1 to len2 do
                let cost = if s1.[i - 1] = s2.[j - 1] then 0 else 1
                d.[i, j] <- min (min (d.[i - 1, j] + 1) (d.[i, j - 1] + 1)) (d.[i - 1, j - 1] + cost)

        d.[len1, len2]

    let calculateSimilarity (s1: string) (s2: string) : float =
        let distance = levenshteinDistance s1 s2
        let maxLen = max (String.length s1) (String.length s2)

        if maxLen = 0 then
            1.0
        else
            1.0 - float distance / float maxLen

    let isSimilarEnough (query: string) (target: string) (threshold: float) : bool =
        let similarity = calculateSimilarity query target
        similarity >= threshold

    let findBestMatch (query: string) (candidates: (string * 'a) list) (threshold: float) : 'a option =
        let scoredCandidates =
            candidates
            |> List.map (fun (name, data) ->
                let similarity = calculateSimilarity query name
                (similarity, data))
            |> List.filter (fun (similarity, _) -> similarity >= threshold)
            |> List.sortByDescending (fun (similarity, _) -> similarity)

        match scoredCandidates with
        | (_, best) :: _ -> Some best
        | [] -> None

// 查询模块
module ModelQuery =
    let mutable private cachedModels: ProviderModels list option = None

    let loadModels (jsonPath: string) : ProviderModels list =
        match cachedModels with
        | Some models -> models
        | None ->
            let models = JsonParser.parseModelsJson jsonPath
            cachedModels <- Some models
            models

    let getAllModels (providers: ProviderModels list) : ModelInfo list =
        providers |> List.collect (fun p -> p.models)

    /// 通过ID精确查询模型
    let queryModelById (providers: ProviderModels list) (modelId: string) : ModelInfo option =
        providers
        |> List.collect (fun p -> p.models)
        |> List.tryFind (fun m -> m.id.Equals(modelId, StringComparison.OrdinalIgnoreCase))

    let queryModelByIdStartOrEnd (providers: ProviderModels list) (modelId: string) : ModelInfo option =
        providers
        |> List.collect (fun p -> p.models)
        |> List.tryFind (fun m ->
            m.id.StartsWith(modelId, StringComparison.OrdinalIgnoreCase)
            || m.id.EndsWith(modelId, StringComparison.OrdinalIgnoreCase))

    /// 通过名称模糊匹配查询模型（threshold: 0.0~1.0，值越高越严格）
    let queryModelByName (providers: ProviderModels list) (name: string) (threshold: float) : ModelInfo option =
        let allModels = getAllModels providers
        let candidates = allModels |> List.map (fun m -> m.name, m)

        FuzzyMatch.findBestMatch name candidates threshold

    /// 通过family查询所有相关模型
    let queryModelsByFamily (providers: ProviderModels list) (family: string) : ModelInfo list =
        providers
        |> List.collect (fun p -> p.models)
        |> List.filter (fun m -> m.family.Equals(family, StringComparison.OrdinalIgnoreCase))

    /// 获取模型的成本信息
    let getCost (model: ModelInfo) : Cost = model.cost

    /// 获取模型的限制信息
    let getLimit (model: ModelInfo) : Limit = model.limit

    /// 根据模型查询输入成本（每1k tokens）
    let getInputCost (model: ModelInfo) : float = model.cost.input

    /// 根据模型查询输出成本（每1k tokens）
    let getOutputCost (model: ModelInfo) : float = model.cost.output

    /// 获取上下文长度（tokens）
    let getContextLength (model: ModelInfo) : int = model.limit.context

    /// 获取输出长度限制（tokens）
    let getOutputLength (model: ModelInfo) : int = model.limit.output

    /// 查询模型的完整信息
    let getModelInfo (providers: ProviderModels list) (query: string) (threshold: float) : ModelInfo option =
        // 先尝试精确匹配
        match queryModelById providers query with
        | Some model -> Some model
        | None ->
            match queryModelByName providers query threshold with
            | Some model -> Some model
            | None -> queryModelByIdStartOrEnd providers query

    /// 按关键字优先级查询模型
    /// 根据modelId中的关键字优先从对应provider中查找
    let queryModelByKeywordPriority (providers: ProviderModels list) (modelId: string) (priorityProviders: string list) (threshold: float) : ModelInfo option =
        // 从优先级provider中查找
        let priorityModels =
            providers
            |> List.filter (fun p -> priorityProviders |> List.exists (fun pp -> p.id.Equals(pp, StringComparison.OrdinalIgnoreCase)))
            |> List.collect (fun p -> p.models)

        // 先尝试精确匹配
        match priorityModels |> List.tryFind (fun m -> m.id.Equals(modelId, StringComparison.OrdinalIgnoreCase)) with
        | Some model -> Some model
        | None ->
            // 然后尝试名称模糊匹配
            let candidates = priorityModels |> List.map (fun m -> m.name, m)
            match FuzzyMatch.findBestMatch modelId candidates threshold with
            | Some model -> Some model
            | None ->
                // 最后尝试前缀/后缀匹配
                match priorityModels |> List.tryFind (fun m ->
                    m.id.StartsWith(modelId, StringComparison.OrdinalIgnoreCase)
                    || m.id.EndsWith(modelId, StringComparison.OrdinalIgnoreCase)) with
                | Some model -> Some model
                | None ->
                    // 如果优先级provider没找到，回退到普通查询
                    getModelInfo providers modelId threshold

    /// 根据modelId自动推断provider优先级并查询
    /// 关键字到provider的映射
    let autoQueryModelWithPriority (providers: ProviderModels list) (modelId: string) (threshold: float) : ModelInfo option =
        let keywordToProvider =
            [ ["gpt-"; "chatgpt"], "openai"
              ["claude-"; "sonnet"; "haiku"; "opus"], "anthropic"
              ["gemini-"], "google"
              ["gemini"], "google"
              ["grok-"], "xai"
              ["mistral-"], "mistral"
              ["llama-"], "meta"
              ["qwen-"], "qwen"
              ["deepseek-"], "deepseek"
              ["o1-"; "o3-"; "o4-"], "openai" ]

        // 查找匹配的关键字对应的provider
        let rec findPriorityProvider keywordsAndProviders =
            match keywordsAndProviders with
            | (keywords, provider) :: rest ->
                if keywords |> List.exists (fun kw -> modelId.StartsWith(kw, StringComparison.OrdinalIgnoreCase)) then
                    Some provider
                else
                    findPriorityProvider rest
            | [] -> None

        match findPriorityProvider keywordToProvider with
        | Some provider -> queryModelByKeywordPriority providers modelId [provider] threshold
        | None -> getModelInfo providers modelId threshold

    /// 拼接成本计算（输入tokens数 + 输出tokens数）
    let calculateCost (model: ModelInfo) (inputTokens: int) (outputTokens: int) : float =
        let inputCost = float inputTokens / 1000.0 * model.cost.input
        let outputCost = float outputTokens / 1000.0 * model.cost.output
        inputCost + outputCost

    /// 查询模型是否支持reasoning
    let supportsReasoning (model: ModelInfo) : bool = model.reasoning

    /// 查询模型是否支持tool calling
    let supportsToolCall (model: ModelInfo) : bool = model.tool_call

// 便捷函数
let getModelsPath () =
    let currentDir = Directory.GetCurrentDirectory()
    Path.Combine(currentDir, "models.json")

let initializeModels () =
    let path = getModelsPath ()

    if File.Exists path then
        ModelQuery.loadModels path
    else
        eprintfn "Warning: models.json not found at %s" path
        []

/// 快速查询模型信息
/// 参数: query - 模型ID或名称
///       threshold - 模糊匹配阈值（0.0~1.0，推荐0.7），0.0为任何匹配都接受
let queryModel (query: string) (threshold: float) : ModelInfo option =
    let providers = initializeModels ()
    ModelQuery.getModelInfo providers query threshold

/// 自动按关键字优先级查询模型（推荐使用）
/// 根据modelId中的关键字自动推断provider并优先查找
/// 例如: "gpt-4o" 会优先从openai provider查找
let queryModelWithPriority (query: string) (threshold: float) : ModelInfo option =
    let providers = initializeModels ()
    ModelQuery.autoQueryModelWithPriority providers query threshold

/// 查询模型成本（返回元组: inputCost, outputCost）
let getModelCost (modelQuery: string) (threshold: float) : (float * float) option =
    match queryModel modelQuery threshold with
    | Some model -> Some(model.cost.input, model.cost.output)
    | None -> None

/// 查询模型限制（返回元组: contextLength, outputLength）
let getModelLimit (modelQuery: string) (threshold: float) : (int * int) option =
    match queryModel modelQuery threshold with
    | Some model -> Some(model.limit.context, model.limit.output)
    | None -> None

/// 计算调用成本
let calculateCallCost (modelQuery: string) (threshold: float) (inputTokens: int) (outputTokens: int) : float option =
    match queryModel modelQuery threshold with
    | Some model -> Some(ModelQuery.calculateCost model inputTokens outputTokens)
    | None -> None

// // 查询模型
// queryModel "gpt-4-turbo" 0.7 |> Option.iter (fun m ->
//     printfn "模型: %s, 输入成本: %.4f/1k" m.name m.cost.input)

// // 获取成本
// getModelCost "claude-3-sonnet" 0.7 |> Option.iter (fun (input, output) ->
//     printfn "成本: input=%.4f, output=%.4f" input output)

// // 计算调用成本
// calculateCallCost "gpt-4" 0.7 1000 500 |> Option.iter (fun cost ->
//     printfn "总成本: ¥%.4f" cost)
