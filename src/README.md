# src/

Source code lives here once milestone **M1 — Foundations** lands.

Planned layout:

```
src/
├── VSMCP.sln
├── VSMCP.Shared/      # netstandard2.0  — DTOs, tool contracts, error codes
├── VSMCP.Server/      # net8.0          — MCP stdio server + pipe client
└── VSMCP.Vsix/        # net472 (VSSDK)  — in-proc VS extension + pipe server
```

See [`../DesignDoc.md`](../DesignDoc.md) §3 for the architecture and §5 for the tool catalog.
