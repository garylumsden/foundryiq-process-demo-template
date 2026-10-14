@export()
type ModelConfig = {
  key: string
  label: string
  role: string
  provider: string
  format: string
  deployment: string
  model: string
  version: string
  sku: string
  capacity: int
  maxOutputTokens: int
  enabled: bool
  capabilities: {
    promptAgent: bool
    mcp: bool
    structuredOutputs: bool
    images: bool
  }
}
