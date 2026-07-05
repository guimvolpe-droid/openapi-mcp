using System.Linq;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using OpenApiMcp;
using Xunit;

public class OpenApiToolFactoryTests
{
    const string Spec = """
    {
      "openapi": "3.0.0",
      "info": { "title": "t", "version": "1" },
      "paths": {
        "/posts": {
          "get": {
            "operationId": "listPosts",
            "summary": "List posts.",
            "parameters": [{ "name": "userId", "in": "query", "schema": { "type": "integer" } }]
          },
          "post": {
            "operationId": "createPost",
            "requestBody": {
              "required": true,
              "content": { "application/json": { "schema": { "type": "object", "properties": { "title": { "type": "string" } } } } }
            }
          }
        },
        "/posts/{id}": {
          "get": {
            "operationId": "getPost",
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } }]
          }
        }
      }
    }
    """;

    static OpenApiDocument Parse() => new OpenApiStringReader().Read(Spec, out _);

    [Fact]
    public void Skips_mutating_operations_by_default()
    {
        var tools = new OpenApiToolFactory().Create(Parse(), new PolicyOptions());
        var names = tools.Select(t => t.Name).ToArray();
        Assert.Contains("getPost", names);
        Assert.Contains("listPosts", names);
        Assert.DoesNotContain("createPost", names);
    }

    [Fact]
    public void Includes_mutating_when_allowed()
    {
        var tools = new OpenApiToolFactory().Create(Parse(), new PolicyOptions { AllowMutating = true });
        Assert.Contains("createPost", tools.Select(t => t.Name));
    }

    [Fact]
    public void Path_param_is_required_and_typed_in_the_input_schema()
    {
        var tools = new OpenApiToolFactory().Create(Parse(), new PolicyOptions());
        var getPost = tools.Single(t => t.Name == "getPost");

        var schema = getPost.InputSchema;
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("id", required);
        Assert.Equal("integer", schema.GetProperty("properties").GetProperty("id").GetProperty("type").GetString());
        Assert.Equal(ParameterLocation.Path, getPost.Parameters.Single().In);
    }

    [Fact]
    public void Allowlist_limits_the_generated_tools()
    {
        var tools = new OpenApiToolFactory().Create(Parse(), new PolicyOptions { AllowedOperations = new() { "getPost" } });
        Assert.Single(tools);
        Assert.Equal("getPost", tools[0].Name);
    }
}
