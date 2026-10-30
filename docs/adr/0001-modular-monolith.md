# ADR 0001: Start as a modular monolith

Status: Accepted

The initial deployment uses one API host and one worker host. Business capabilities remain isolated by folders and namespaces inside `Loyalty.Core`, rather than by individual projects. This keeps the solution small while preserving boundaries that can be extracted later when scale or team ownership requires it.
