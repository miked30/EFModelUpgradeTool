using System;
using System.Xml.Linq;
using System.Linq;

namespace EdmxForeignKeyMapper
{
    public class SchemaProcessor
    {
        public static readonly XNamespace CodeNS = "http://schemas.microsoft.com/ado/2008/09/edm";
        public static readonly XNamespace MappingNS = "http://schemas.microsoft.com/ado/2008/09/mapping/cs";

        private readonly XElement schema;
        private readonly XElement mappings;
        private readonly string entityNamespace;
        private readonly XElement container;

        public SchemaProcessor(XElement schema, XElement mappings)
        {
            this.schema = schema;
            this.mappings = mappings;
            entityNamespace = schema.Attribute("Namespace").Value;
            container = schema.Element(CodeNS + "EntityContainer");
        }

        public void Process()
        {
            var entityTypes = schema.Elements(CodeNS + "EntityType");
            foreach (var entityType in entityTypes)
            {
                ProcessEntityType(entityType);
            }
        }

        private void ProcessEntityType(XElement entityType)
        {
            var navigationProperties = entityType.Elements(CodeNS + "NavigationProperty");
            var entityName = entityNamespace + "." + entityType.Attribute("Name").Value;

            foreach (var property in navigationProperties)
            {
                ProcessNavigationProperty(property, entityName, entityType);
            }
        }

        private void ProcessNavigationProperty(XElement property, string entityName, XElement entityType)
        {
            var relationship = property.Attribute("Relationship").Value;
            var associationSet = container.Elements(CodeNS + "AssociationSet").Single(e => e.Attribute("Association").Value == relationship);
            var associationSetName = associationSet.Attribute("Name").Value;
            var association = schema.Elements(CodeNS + "Association").Single(a => a.Attribute("Name").Value == associationSetName);

            // Determine if this Association has already had a referential constrain added
            if (association.Element(CodeNS + "ReferentialConstraint") != null)
                return;

            // Determine multiplicity
            var fromRole = property.Attribute("FromRole").Value;
            var toRole = property.Attribute("ToRole").Value;
            var toRoleMultiplicity = GetMultiplicity(association, toRole);
            var fromRoleMultiplicity = GetMultiplicity(association, fromRole);

            // We can't create an ID property in these scenarios
            if (toRoleMultiplicity == "*")
                return;
            if (fromRoleMultiplicity == "1" && toRoleMultiplicity == "1")
            {
                // Adding a constraint in this case breaks the model as EF doesn't support constraints on 1-1 mappings
                // See http://weblogs.asp.net/manavi/archive/2011/05/01/associations-in-ef-4-1-code-first-part-5-one-to-one-foreign-key-associations.aspx
                return;
            }

            // Get the primary key of the linked table
            var primaryKeyName = GetPrimaryKeyName(associationSet, toRole);

            // Find the database column name for this association set
            var associationSetMapping = mappings.Descendants(MappingNS + "AssociationSetMapping").Single(e => e.Attribute("Name").Value == associationSetName);
            var fromEndProperty = associationSetMapping.Elements(MappingNS + "EndProperty").Single(e => e.Attribute("Name").Value == toRole);
            var dbColumnName = fromEndProperty.Element(MappingNS + "ScalarProperty").Attribute("ColumnName").Value;

            // Ensure that the new property name doesn't match the navigation property name and is unique
            int suffixCount = 1;
            var newPropertyName = dbColumnName;
            while (property.Attribute("Name").Value == newPropertyName
                   || entityContainsPropertyWithSameName(newPropertyName, entityType))
            {
                if (!newPropertyName.EndsWith("ID", StringComparison.OrdinalIgnoreCase))
                {
                    newPropertyName = dbColumnName + "ID";
                }
                else
                {
                    newPropertyName = dbColumnName + suffixCount;
                    suffixCount++;
                }
            }

            // All the data has been obtained so update the model

            // Create new property for the foreign key id
            entityType.Add(CreateNewProperty(newPropertyName, toRoleMultiplicity));

            // Add a referential constraint element to association
            association.Add(CreateNewConstraint(toRole, primaryKeyName, fromRole, newPropertyName));

            // Add a mapping for the new property
            var typeMapping = mappings.Descendants(MappingNS + "EntityTypeMapping").Single(e => e.Attribute("TypeName").Value == entityName
                || e.Attribute("TypeName").Value == "IsTypeOf(" + entityName + ")");
            typeMapping.Element(MappingNS + "MappingFragment").Add(CreateNewMapping(newPropertyName, dbColumnName));

            // Remove the now redundant AssociationSetMapping element
            associationSetMapping.Remove();
        }

        private bool entityContainsPropertyWithSameName(string newPropertyName, XElement entityType)
        {
            return entityType.Elements().Any(e => e.Name.LocalName.EndsWith("Property")
                && e.Attribute("Name") != null
                && e.Attribute("Name").Value == newPropertyName);
        }

        private static XElement CreateNewMapping(string propertyName, string dbColumnName)
        {
            return new XElement(MappingNS + "ScalarProperty", new XAttribute("Name", propertyName), new XAttribute("ColumnName", dbColumnName));
        }

        private static XElement CreateNewConstraint(string toRole, string primaryKeyName, string fromRole, string newPropertyName)
        {
            var newConstraint = new XElement(CodeNS + "ReferentialConstraint",
                                             new XElement(CodeNS + "Principal", new XAttribute("Role", toRole),
                                                          new XElement(CodeNS + "PropertyRef", new XAttribute("Name", primaryKeyName))),
                                             new XElement(CodeNS + "Dependent", new XAttribute("Role", fromRole),
                                                          new XElement(CodeNS + "PropertyRef", new XAttribute("Name", newPropertyName)))
                );
            return newConstraint;
        }

        private static XElement CreateNewProperty(string newPropertyName, string multiplicity)
        {
            var idProperty = new XElement(CodeNS + "Property", new XAttribute("Type", "Int32"), new XAttribute("Name", newPropertyName));
            if (multiplicity == "1")
            {
                // The ID can't be nullable in this scenario
                idProperty.SetAttributeValue("Nullable", "false");
            }
            return idProperty;
        }

        private static string GetMultiplicity(XElement association, string toRole)
        {
            var associationEnd = association.Elements(CodeNS + "End").Single(e => e.Attribute("Role").Value == toRole);
            var multiplicity = associationEnd.Attribute("Multiplicity").Value;
            return multiplicity;
        }

        private string GetPrimaryKeyName(XElement associationSet, string toRole)
        {
            var associatedEntityRole = associationSet.Elements(CodeNS + "End").Single(e => e.Attribute("Role").Value == toRole);
            var associatedEntitySetName = associatedEntityRole.Attribute("EntitySet").Value;
            var keyEntitySet = container.Elements(CodeNS + "EntitySet").Single(e => e.Attribute("Name").Value == associatedEntitySetName);
            var keyEntityType = keyEntitySet.Attribute("EntityType").Value;

            // Check the namespace against our assumptions
            if (!keyEntityType.Contains(schema.Attribute("Namespace").Value) || !keyEntityType.Contains('.'))
                throw new Exception("Target entity is either not in a namespace or in another namespace. These cases aren't handled.");

            var keyEntityName = keyEntityType.Substring(keyEntityType.IndexOf('.') + 1);
            var primaryKeyEntity = schema.Elements(CodeNS + "EntityType").Single(e => e.Attribute("Name").Value == keyEntityName);
            var primaryKeyElement = primaryKeyEntity.Element(CodeNS + "Key");
            if (primaryKeyElement == null)
                throw new Exception("No primary key found for entity " + keyEntityName);
            var primaryKeyName = primaryKeyElement.Elements(CodeNS + "PropertyRef").First().Attribute("Name").Value;
            return primaryKeyName;
        }
    }
}