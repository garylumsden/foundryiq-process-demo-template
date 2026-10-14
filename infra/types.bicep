@export()
type ModelConfig = {
  key: string
  label: string
  role: string
  provider: 'openai' | 'anthropic'
  deployment: string
  model: string
  version: string
  sku: string
  capacity: int
  maxOutputTokens: int
  enabled: bool
}
