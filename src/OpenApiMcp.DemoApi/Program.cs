using OpenApiMcp.DemoApi;

// API sintética de demonstração — o upstream offline do openapi-mcp. Espelha a superfície
// do demo público (posts), com paginação via Link header e seed determinístico.
await DemoApp.Build(args).RunAsync();
