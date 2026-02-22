# AGENTS.md - OpenCode Autoconfig Project

## Project Overview

This project manages OpenCode AI assistant configuration by fetching available AI models from various API endpoints and generating the `opencode.jsonc` configuration file. It uses F# scripts (.fsx) for data processing and JSON manipulation.

**Primary Language:** F# (.fsx files)  
**Configuration Format:** JSONC (JSON with Comments)  
**Purpose:** Automate OpenCode model configuration management

---

## Build & Run Commands

### Fetch Models (Main Operation)

```bash
# Option 1: Direct F# script execution
dotnet fsi fetch-models.fsx

# Option 2: Windows batch script
fetch-models.cmd
```

### Prerequisites

1. **.NET SDK** - Required to run F# scripts
   - Install via: https://dotnet.microsoft.com/download

2. **env.json** - Create from `env.json.example` with your API endpoints:
   ```json
   {
     "endpoints": [
       {
         "key": "home-openai",
         "name": "Home-OpenAI",
         "npm": "@ai-sdk/openai-compatible",
         "baseUrl": "https://your-api-url/v1",
         "apiKey": "your-api-key",
         "whitelist": ["gpt-.*", "claude-.*"],
         "blacklist": []
       }
     ]
   }
   ```

### Output

- Generates `opencode.jsonc` with model configurations from all endpoints
- Models are filtered by whitelist/blacklist regex patterns
- Copy output to `~/.config/opencode/opencode.jsonc` (Linux) or `%APPDATA%\opencode\opencode.jsonc` (Windows)

---

## Code Style Guidelines

### F# Script Conventions

**File Organization:**
- Type definitions at top (records, types)
- Module definitions for pure functions
- Main execution logic at bottom
- Use `//` for single-line comments, `(* ... *)` for block comments

**Naming:**
- PascalCase for types, modules, functions
- camelCase for local variables and function parameters
- Use descriptive names: `fetchModels` not `fm`
- Chinese comments OK for Chinese developers (project uses Chinese comments)

**Type Definitions:**
```fsharp
// Record types for structured data
type Cost = {
    input: float
    output: float
}

type ModelInfo = {
    raw: JsonElement
    id: string
    name: string
    family: string
}
```

**Functional Patterns:**
- Use pattern matching for conditionals
- Prefer pipeline operators (`|>`) for data transformation
- Use `Option` type for nullable values
- Avoid mutable state unless necessary

```fsharp
// Good: Pipeline with pattern matching
models
|> Array.filter (fun m -> m.id.StartsWith "gpt-")
|> Array.map (fun m -> m.id)

// Good: Pattern matching with Option
match parseJsonElement element key with
| Some el -> el.GetString()
| None -> defaultValue
```

**Async/Tasks:**
- Use `task { }` computation expression for async operations
- Use `async { }` for CPU-bound parallelism
- Always handle exceptions with try-catch

```fsharp
let fetchModels (baseUrl: string) (apiKey: string) =
    task {
        use client = new HttpClient()
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}")
        let! response = client.GetAsync $"{baseUrl}/models"
        response.EnsureSuccessStatusCode() |> ignore
        let! content = response.Content.ReadAsStringAsync()
        return content
    }
```

---

## JSONC Configuration

### Structure

The `opencode.jsonc` follows OpenCode's configuration schema:

```jsonc
{
  // Model settings
  "model": "kiro-claude-sonnet-4-5-thinking",
  "theme": "opencode",
  "autoupdate": true,

  // Agent-specific models
  "agent": {
    "title": { "model": "..." },
    "build": { "model": "..." },
    "explore": { "model": "..." },
    "plan": { "model": "..." }
  },

  // Plugins
  "plugin": [
    "oh-my-opencode@latest",
    "@tarquinen/opencode-dcp@latest"
  ],

  // MCP servers (Model Context Protocol)
  "mcp": {
    "playwright": { ... }
  },

  // API Providers
  "provider": {
    "home-openai": { ... },
    "home-google": { ... },
    "home-claude": { ... }
  }
}
```

### Model Entry Schema

Each model entry should include:
```jsonc
{
  "id": "model-id",
  "name": "Display Name",
  "family": "model-family",
  "attachment": true,
  "reasoning": true,
  "tool_call": true,
  "temperature": true,
  "cost": {
    "input": 0.0,
    "output": 0.0
  },
  "limit": {
    "context": 128000,
    "output": 16384
  }
}
```

---

## Error Handling

- Always wrap API calls in try-catch blocks
- Print meaningful error messages with context
- Use exit codes: 0 for success, 1 for failure
- Log warning for non-critical issues (missing optional data)

```fsharp
try
    // Operation
with
| ex ->
    printfn "Error: %s" ex.Message
    return 1
```

---

## Important Files

| File | Purpose |
|------|---------|
| `fetch-models.fsx` | Main script - fetches models from APIs |
| `models.fsx` | Type definitions and model query logic |
| `opencode.jsonc` | Generated OpenCode configuration |
| `env.json` | API endpoint configuration (NOT committed) |
| `env.json.example` | Template for env.json |

---

## Git Workflow

**DO NOT COMMIT:**
- `env.json` - Contains API keys
- `opencode.jsonc` - Generated, user-specific config
- `.env` files

**Safe to version:**
- `fetch-models.fsx` - Main logic
- `models.fsx` - Type definitions
- `env.json.example` - Template
- This AGENTS.md

---

## Testing

This project does not have automated tests. Manual verification:
1. Run `dotnet fsi fetch-models.fsx`
2. Verify `opencode.jsonc` is generated correctly
3. Validate JSON structure
4. Copy to OpenCode config directory
5. Verify models appear in OpenCode model selector
