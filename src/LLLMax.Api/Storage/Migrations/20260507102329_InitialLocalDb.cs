using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LLLMax.Api.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialLocalDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS api_integrations (
                    id TEXT NOT NULL PRIMARY KEY,
                    name TEXT NOT NULL,
                    base_url TEXT NOT NULL,
                    open_api_url TEXT NULL,
                    discovered_at TEXT NOT NULL,
                    operations_json TEXT NOT NULL,
                    raw_schema TEXT NULL
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS app_metadata (
                    key TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS approvals (
                    id TEXT NOT NULL PRIMARY KEY,
                    kind TEXT NOT NULL,
                    status TEXT NOT NULL,
                    title TEXT NOT NULL,
                    description TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    conversation_id TEXT NULL,
                    decision_reason TEXT NULL,
                    scope TEXT NULL
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS background_jobs (
                    id TEXT NOT NULL PRIMARY KEY,
                    kind TEXT NOT NULL,
                    status TEXT NOT NULL,
                    title TEXT NULL,
                    session_id TEXT NULL,
                    agent TEXT NULL,
                    payload_json TEXT NOT NULL,
                    progress_current INTEGER NOT NULL,
                    progress_total INTEGER NOT NULL,
                    status_message TEXT NULL,
                    result TEXT NULL,
                    error TEXT NULL,
                    notify_session INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    completed_at TEXT NULL
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS mcp_servers (
                    id TEXT NOT NULL PRIMARY KEY,
                    name TEXT NOT NULL,
                    transport TEXT NOT NULL,
                    endpoint TEXT NOT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS memory_consolidation_jobs (
                    id TEXT NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    memories_written INTEGER NOT NULL,
                    summary TEXT NULL,
                    error TEXT NULL,
                    task_graph_id TEXT NULL
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS sessions (
                    id TEXT NOT NULL PRIMARY KEY,
                    title TEXT NOT NULL,
                    agent TEXT NOT NULL,
                    model TEXT NULL,
                    summary TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS task_graphs (
                    id TEXT NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL,
                    goal TEXT NOT NULL,
                    status TEXT NOT NULL,
                    active_node_id TEXT NULL,
                    confidence REAL NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    graph_json TEXT NOT NULL
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS mcp_tools (
                    server_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL,
                    arguments_json_schema TEXT NOT NULL,
                    approval_status TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    PRIMARY KEY (server_id, name),
                    FOREIGN KEY (server_id) REFERENCES mcp_servers(id) ON DELETE CASCADE
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS session_messages (
                    session_id TEXT NOT NULL,
                    ordinal INTEGER NOT NULL,
                    role TEXT NOT NULL,
                    content TEXT NOT NULL,
                    trace_id TEXT NULL,
                    reasoning_steps_json TEXT NULL,
                    task_graph_json TEXT NULL,
                    PRIMARY KEY (session_id, ordinal),
                    FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
                );
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS IX_api_integrations_name ON api_integrations (name);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_approvals_updated_at ON approvals (updated_at);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_background_jobs_updated_at ON background_jobs (updated_at);");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS IX_mcp_servers_name ON mcp_servers (name);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_memory_consolidation_jobs_updated_at ON memory_consolidation_jobs (updated_at);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_sessions_updated_at ON sessions (updated_at);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_task_graphs_updated_at ON task_graphs (updated_at);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_integrations");

            migrationBuilder.DropTable(
                name: "app_metadata");

            migrationBuilder.DropTable(
                name: "approvals");

            migrationBuilder.DropTable(
                name: "background_jobs");

            migrationBuilder.DropTable(
                name: "mcp_tools");

            migrationBuilder.DropTable(
                name: "memory_consolidation_jobs");

            migrationBuilder.DropTable(
                name: "session_messages");

            migrationBuilder.DropTable(
                name: "task_graphs");

            migrationBuilder.DropTable(
                name: "mcp_servers");

            migrationBuilder.DropTable(
                name: "sessions");
        }
    }
}
