# openapi-mcp — MCP stdio server. Build once, run anywhere (no libicu needed).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/OpenApiMcp.Server/OpenApiMcp.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
# Runs without ICU (matches the csproj setting); keeps the image behaviour deterministic.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
# The server speaks MCP over stdio — run with:  docker run -i --rm openapi-mcp
ENTRYPOINT ["dotnet", "OpenApiMcp.Server.dll"]
