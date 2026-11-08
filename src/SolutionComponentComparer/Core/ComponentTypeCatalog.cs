using System.Collections.Generic;

namespace SolutionComponentComparer.Core
{
    /// <summary>
    /// Static knowledge about solution component types (solutioncomponent.componenttype).
    /// Types above ~10000 are environment specific and are resolved at runtime through
    /// the solutioncomponentdefinition table.
    /// </summary>
    public static class ComponentTypeCatalog
    {
        public const int Entity = 1;
        public const int Attribute = 2;
        public const int OptionSet = 9;
        public const int EntityRelationship = 10;
        public const int EntityKey = 14;
        public const int SavedQuery = 26;
        public const int SavedQueryVisualization = 59;
        public const int SystemForm = 60;

        public static readonly Dictionary<int, string> TypeNames = new Dictionary<int, string>
        {
            { 1, "Table" },
            { 2, "Column" },
            { 3, "Relationship" },
            { 4, "Attribute Picklist Value" },
            { 5, "Attribute Lookup Value" },
            { 6, "View Attribute" },
            { 7, "Localized Label" },
            { 8, "Relationship Extra Condition" },
            { 9, "Choice (Option Set)" },
            { 10, "Table Relationship" },
            { 11, "Relationship Role" },
            { 12, "Relationship Relationships" },
            { 13, "Managed Property" },
            { 14, "Table Key" },
            { 16, "Privilege" },
            { 17, "Privilege Object Type Code" },
            { 18, "Index" },
            { 20, "Security Role" },
            { 21, "Role Privilege" },
            { 22, "Display String" },
            { 23, "Display String Map" },
            { 24, "Form (legacy)" },
            { 25, "Organization" },
            { 26, "View" },
            { 29, "Process / Workflow" },
            { 31, "Report" },
            { 32, "Report Entity" },
            { 33, "Report Category" },
            { 34, "Report Visibility" },
            { 35, "Attachment" },
            { 36, "Email Template" },
            { 37, "Contract Template" },
            { 38, "KB Article Template" },
            { 39, "Mail Merge Template" },
            { 44, "Duplicate Rule" },
            { 45, "Duplicate Rule Condition" },
            { 46, "Entity Map" },
            { 47, "Attribute Map" },
            { 48, "Ribbon Command" },
            { 49, "Ribbon Context Group" },
            { 50, "Ribbon Customization" },
            { 52, "Ribbon Rule" },
            { 53, "Ribbon Tab To Command Map" },
            { 55, "Ribbon Diff" },
            { 59, "Chart" },
            { 60, "Form" },
            { 61, "Web Resource" },
            { 62, "Site Map" },
            { 63, "Connection Role" },
            { 64, "Complex Control" },
            { 65, "Hierarchy Rule" },
            { 66, "Custom Control (PCF)" },
            { 68, "Custom Control Default Config" },
            { 70, "Field Security Profile" },
            { 71, "Field Permission" },
            { 80, "Model-driven App" },
            { 90, "Plugin Type" },
            { 91, "Plugin Assembly" },
            { 92, "Plugin Step" },
            { 93, "Plugin Step Image" },
            { 95, "Service Endpoint" },
            { 150, "Routing Rule" },
            { 151, "Routing Rule Item" },
            { 152, "SLA" },
            { 153, "SLA Item" },
            { 154, "Convert Rule" },
            { 155, "Convert Rule Item" },
            { 161, "Mobile Offline Profile" },
            { 162, "Mobile Offline Profile Item" },
            { 165, "Similarity Rule" },
            { 166, "Data Source Mapping" },
            { 201, "SDK Message" },
            { 202, "SDK Message Filter" },
            { 203, "SDK Message Pair" },
            { 204, "SDK Message Request" },
            { 205, "SDK Message Request Field" },
            { 206, "SDK Message Response" },
            { 207, "SDK Message Response Field" },
            { 208, "Import Map" },
            { 210, "Web Wizard" },
            { 300, "Canvas App" },
            { 371, "Connector" },
            { 372, "Connector" },
            { 380, "Environment Variable Definition" },
            { 381, "Environment Variable Value" },
            { 400, "AI Project Type" },
            { 401, "AI Project" },
            { 402, "AI Configuration" },
            { 430, "Entity Analytics Configuration" },
            { 431, "Attribute Image Configuration" },
            { 432, "Entity Image Configuration" }
        };

        /// <summary>Fallback map: component type -> table that stores the component records.</summary>
        public static readonly Dictionary<int, string> EntityByType = new Dictionary<int, string>
        {
            { 20, "role" },
            { 26, "savedquery" },
            { 29, "workflow" },
            { 31, "report" },
            { 36, "template" },
            { 37, "contracttemplate" },
            { 38, "kbarticletemplate" },
            { 39, "mailmergetemplate" },
            { 44, "duplicaterule" },
            { 46, "entitymap" },
            { 59, "savedqueryvisualization" },
            { 60, "systemform" },
            { 61, "webresource" },
            { 62, "sitemap" },
            { 63, "connectionrole" },
            { 65, "hierarchyrule" },
            { 66, "customcontrol" },
            { 70, "fieldsecurityprofile" },
            { 80, "appmodule" },
            { 90, "plugintype" },
            { 91, "pluginassembly" },
            { 92, "sdkmessageprocessingstep" },
            { 93, "sdkmessageprocessingstepimage" },
            { 95, "serviceendpoint" },
            { 150, "routingrule" },
            { 152, "sla" },
            { 154, "convertrule" },
            { 161, "mobileofflineprofile" },
            { 165, "similarityrule" },
            { 208, "importmap" },
            { 300, "canvasapp" },
            { 371, "connector" },
            { 372, "connector" },
            { 380, "environmentvariabledefinition" },
            { 381, "environmentvariablevalue" }
        };

        /// <summary>Component types whose records point to the table they belong to.</summary>
        public static readonly Dictionary<int, string> ParentEntityAttribute = new Dictionary<int, string>
        {
            { 26, "returnedtypecode" },
            { 29, "primaryentity" },
            { 46, "sourceentityname" },
            { 59, "primaryentitytypecode" },
            { 60, "objecttypecode" }
        };

        /// <summary>Types whose names come from table metadata rather than from records.</summary>
        public static bool IsMetadataType(int type)
        {
            return type == Entity || type == Attribute || type == OptionSet || type == EntityRelationship || type == EntityKey;
        }

        public static string GetTypeName(int type, string serverLabel)
        {
            string name;
            if (TypeNames.TryGetValue(type, out name)) return name;
            if (!string.IsNullOrWhiteSpace(serverLabel)) return serverLabel;
            return "Type " + type;
        }

        public static string BehaviorText(int? behavior)
        {
            if (!behavior.HasValue) return string.Empty;
            switch (behavior.Value)
            {
                case 0: return "All subcomponents";
                case 1: return "No subcomponents";
                case 2: return "Shell only";
                default: return "Behavior " + behavior.Value;
            }
        }

        /// <summary>How much of a root component a solution carries. Higher = more.</summary>
        public static int BehaviorBreadth(int? behavior)
        {
            if (!behavior.HasValue) return 2;
            switch (behavior.Value)
            {
                case 0: return 3;
                case 1: return 2;
                case 2: return 1;
                default: return 2;
            }
        }
    }
}
