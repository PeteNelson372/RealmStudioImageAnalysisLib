namespace RealmStudioImageAnalysisLib
{
    using RealmStudioShapeRenderingLib;
    using SharpVectors.Dom;
    using System.Xml.Linq;

    public sealed class PerimeterPipelineLoader
    {
        public PerimeterPipelineDefinition LoadDefinition(string fileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

            var document = XDocument.Load(fileName);

            var root = document.Root
                ?? throw new InvalidDataException("Pipeline XML does not contain a root element.");

            if (!string.Equals(
                    root.Name.LocalName,
                    "Pipeline",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The root element must be <Pipeline>.");
            }

            var name = (string?)root.Attribute("name");

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException("The Pipeline element must have a 'name' attribute.");
            }

            PerimeterPipelineType type = PerimeterPipelineType.Extraction;

            string? typeAttribute = root.Attribute("type")?.Value;

            if (!string.IsNullOrWhiteSpace(typeAttribute))
            {
                if (!Enum.TryParse(typeAttribute, ignoreCase: true, out type))
                {
                    throw new InvalidDataException($"The Pipeline element has an invalid 'type' attribute value '{typeAttribute}'.");
                }
            }

            bool debug = false;

            string? debugAttribute = root.Attribute("debug")?.Value;

            if (!string.IsNullOrWhiteSpace(debugAttribute))
            {
                debug = bool.TryParse(debugAttribute, out var result) && result;
            }

            int priority = 0;

            string? priorityAttribute = root.Attribute("priority")?.Value;

            if (!string.IsNullOrWhiteSpace(priorityAttribute))
            {
                priority = int.TryParse(priorityAttribute, out var result) ? result : 0;
            }

            var definition = new PerimeterPipelineDefinition
            {
                Name = name,
                Type = type,
                Debug = debug,
                Priority = priority
            };

            foreach (var stageElement in root.Elements("Stage"))
            {
                definition.Stages.Add(LoadStageDefinition(stageElement));
            }

            if (definition.Stages.Count == 0)
            {
                throw new InvalidDataException($"Pipeline '{name}' does not contain any stages.");
            }

            return definition;
        }

        private static PerimeterStageDefinition LoadStageDefinition(XElement element)
        {
            var id = (string?)element.Attribute("id");

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException("A Stage element is missing its 'id' attribute.");
            }

            var definition = new PerimeterStageDefinition
            {
                Id = id,

                SaveOutput = ParseBooleanAttribute(element, "saveOutput", defaultValue: false),

                OutputArtifact = (string?)element.Attribute("outputArtifact"),

                OutputName = (string?)element.Attribute("outputName")
            };

            string? inputArtifact = element.Attribute("inputArtifact")?.Value;

            if (!string.IsNullOrWhiteSpace(inputArtifact))
            {
                definition.InputArtifacts.Add(inputArtifact);
            }

            foreach (XElement inputElement in element.Elements("InputArtifact"))
            {
                string? name = inputElement.Attribute("name")?.Value;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    definition.InputArtifacts.Add(name);
                }
            }

            foreach (var parameter in element.Elements("Parameter"))
            {
                var name = (string?)parameter.Attribute("name");
                var value = (string?)parameter.Attribute("value");

                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidDataException($"Stage '{id}' contains a Parameter without a name.");
                }

                if (value is null)
                {
                    throw new InvalidDataException($"Stage '{id}' parameter '{name}' does not have a value.");
                }

                definition.Parameters[name] = value;
            }

            return definition;
        }

        private static bool ParseBooleanAttribute(XElement element, string name, bool defaultValue)
        {
            var value = (string?)element.Attribute(name);

            if (value is null)
                return defaultValue;

            if (bool.TryParse(value, out var result))
                return result;

            throw new InvalidDataException(
                $"Element '{element.Name}' has an invalid boolean " +
                $"value '{value}' for attribute '{name}'.");
        }
    }
}

