# An MCP tool failure only reaches the caller if it is an McpException

The ModelContextProtocol C# SDK answers a throwing tool with the blanket string
`An error occurred invoking '<tool>'.` for every exception type **except**
`McpException`, whose `Message` it propagates verbatim into the `CallToolResult`.
A tool that throws `DbUpdateException`, `InvalidOperationException`, anything —
tells the caller nothing, and the server-side cause is invisible unless something
logged it. That is what made issue #626 (`save_thesis` failing on every write)
undiagnosable from the agent side.

`FinanceSentry.Mcp.Middleware.McpToolErrorSurfacing` closes this once for all
tools: a single call-tool filter (`WithToolErrorSurfacing()`, installed on both
the stdio and HTTP builders in `Program.cs`) logs the full exception and rethrows
it as an `McpException` naming the type, the message and the innermost message.
Do **not** add per-tool try/catch for this — the seam already exists.

Consequence when writing a tool: throw whatever is natural. Throw `McpException`
directly only for a message you have *deliberately* worded for the model, since
the filter leaves those untouched.
