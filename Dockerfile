# openapi-mcp — MCP server (stdio por padrão; Streamable HTTP com OPENAPI_MCP_HTTP=1).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/OpenApiMcp.Server/OpenApiMcp.Server.csproj -c Release -o /app

# Base aspnet (não runtime): o servidor referencia Microsoft.AspNetCore.App (transporte HTTP);
# sem o shared framework ASP.NET a app nem sobe.
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# Runs without ICU (matches the csproj setting); keeps the image behaviour deterministic.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 8410
# stdio:  docker run -i --rm openapi-mcp
# http:   ver docker-compose.yml (OPENAPI_MCP_HTTP=1 + token + URL de bind)
ENTRYPOINT ["dotnet", "OpenApiMcp.Server.dll"]
