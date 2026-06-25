#!/usr/bin/env ruby
# frozen_string_literal: true

require "json"
require "open-uri"
require "yaml"

SOURCE_URL = ENV.fetch("CLOCKODO_OPENAPI_URL", "https://docs.clockodo.com/openapi.yaml")
REPO_ROOT = File.expand_path("..", __dir__)
OUTPUT_PATH = File.join(REPO_ROOT, "src", "Clockodo.Mcp", "ClockodoOperationCatalog.Generated.cs")

def csharp_string(value)
  value.nil? ? "null" : JSON.generate(value)
end

def schema_summary(schema)
  return nil unless schema

  if schema["$ref"]
    return { "$ref" => schema["$ref"].split("/").last }
  end

  summary = {}
  %w[type format description maximum minimum maxLength enum examples style explode].each do |key|
    summary[key] = schema[key] if schema.key?(key)
  end

  if schema["items"]
    summary["items"] = schema_summary(schema["items"])
  end

  if schema["oneOf"]
    summary["oneOf"] = schema["oneOf"].map { |item| schema_summary(item) }
  end

  if schema["properties"]
    summary["properties"] = schema["properties"].transform_values { |property_schema| schema_summary(property_schema) }
  end

  summary.empty? ? nil : summary
end

def request_body_summary(operation)
  request_body = operation["requestBody"]
  return nil unless request_body

  schema = request_body.dig("content", "application/json", "schema")
  return { "required" => request_body["required"] == true } unless schema

  properties = schema.fetch("properties", {}).transform_values { |property_schema| schema_summary(property_schema) }
  {
    "required" => request_body["required"] == true,
    "requiredProperties" => schema["required"] || [],
    "properties" => properties
  }
end

yaml = URI.open(SOURCE_URL, read_timeout: 30, &:read)
doc = YAML.safe_load(yaml, aliases: true)
version = doc.dig("info", "version") || "unknown"

operations = []
doc.fetch("paths").each do |path, methods|
  methods.each do |method, operation|
    next unless %w[get post put delete patch].include?(method)

    parameters = operation["parameters"] || []

    operations << {
      operation_id: operation.fetch("operationId"),
      method: method.upcase,
      path: path,
      tag: (operation["tags"] || ["Other"]).first,
      summary: operation["summary"],
      parameters: parameters.map { |parameter| parameter.fetch("name") },
      parameter_details: parameters.map do |parameter|
        {
          "name" => parameter.fetch("name"),
          "in" => parameter.fetch("in"),
          "required" => parameter["required"] == true,
          "style" => parameter["style"],
          "explode" => parameter["explode"],
          "schema" => schema_summary(parameter["schema"])
        }.compact
      end,
      requires_body: operation.dig("requestBody", "required") == true,
      request_body: request_body_summary(operation),
      deprecated: operation["deprecated"] == true
    }
  end
end

lines = []
lines << "// Generated from #{SOURCE_URL} (version #{version})."
lines << "namespace Clockodo.Mcp;"
lines << ""
lines << "public static partial class ClockodoOperationCatalog"
lines << "{"
lines << "    public const string OpenApiVersion = #{csharp_string(version)};"
lines << "    public const string SourceUrl = #{csharp_string(SOURCE_URL)};"
lines << ""
lines << "    public static IReadOnlyList<ClockodoOperation> All { get; } = new ClockodoOperation[]"
lines << "    {"

operations.each do |operation|
  parameters = if operation[:parameters].empty?
                 "Array.Empty<string>()"
               else
                 "new[] { #{operation[:parameters].map { |parameter| csharp_string(parameter) }.join(", ")} }"
               end

  lines << "        new(#{csharp_string(operation[:operation_id])}, #{csharp_string(operation[:method])}, #{csharp_string(operation[:path])}, #{csharp_string(operation[:tag])}, #{csharp_string(operation[:summary])}, #{parameters}, #{csharp_string(JSON.generate(operation[:parameter_details]))}, #{operation[:requires_body]}, #{csharp_string(operation[:request_body] && JSON.generate(operation[:request_body]))}, #{operation[:deprecated]}),"
end

lines << "    };"
lines << "}"

File.write(OUTPUT_PATH, lines.join("\n") + "\n")
puts "Wrote #{operations.count} operations from Clockodo OpenAPI #{version} to #{OUTPUT_PATH}"
