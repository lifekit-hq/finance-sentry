/**
 * Permission claim values the API grants per user (role bundle plus per-person grants), as returned in
 * `UserDto.permissions`. The frontend uses them to hide navigation and guard routes; the API enforces them.
 */
export enum Permission {
  ConnectionsManage = 'connections.manage',
  AiUse = 'ai.use',
  McpConnect = 'mcp.connect',
  McpService = 'mcp.service',
  OpsAdmin = 'ops.admin',
  UsersManage = 'users.manage',
}
