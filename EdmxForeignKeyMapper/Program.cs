using System;
using System.IO;
using System.Xml.Linq;
using System.Linq;

namespace EdmxForeignKeyMapper
{
    static class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0 || !File.Exists(args[0]))
            {
                Console.WriteLine("Please provide the path to an EDMX file as the first argument");
            }

            ProcessFile(args[0]);
        }

        private static void ProcessFile(string edmxFile)
        {
            var model = XDocument.Load(edmxFile);
            var mappings = model.Descendants(SchemaProcessor.MappingNS + "Mapping").First();

            // Find all Schema elements
            var schemaElements = model.Descendants().Where(e => e.Name == SchemaProcessor.CodeNS + "Schema" ).Select(e => e);
            foreach (var schema in schemaElements)
            {
                var schemaProcessor = new SchemaProcessor(schema, mappings);
                schemaProcessor.Process();
            }

            // Ensure that the designer flag is set for including foreign keys in model in future model updates
            XNamespace designerNS = "http://schemas.microsoft.com/ado/2008/10/edmx";
            var includeForeignKeysProperty = model.Descendants(designerNS + "DesignerProperty").SingleOrDefault(e => e.Attribute("Name").Value == "IncludeForeignKeysInModel");
            if (includeForeignKeysProperty == null)
            {
                includeForeignKeysProperty = new XElement(designerNS + "DesignerProperty", new XAttribute("Name", "IncludeForeignKeysInModel"), new XAttribute("Value", "True"));
                model.Descendants(designerNS + "Options").First().Add(includeForeignKeysProperty);
            }
            else
            {
                includeForeignKeysProperty.Attribute("Value").SetValue("True");
            }

            // Save the changes
            model.Save(edmxFile);
        }
    }
}
