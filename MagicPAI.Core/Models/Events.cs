// Re-export shared model types so existing "using MagicPAI.Core.Models;" code continues to work.
// The actual definitions are in MagicPAI.Shared.Models.
global using MagicPAI.Shared.Models;

namespace MagicPAI.Core.Models;

// Type aliases for backward compatibility — all types are now defined in MagicPAI.Shared.Models
// and automatically available via the global using above.
