using System.Collections.Generic;

namespace SolutionComponentComparer.UI
{
    /// <summary>User settings, stored per connection so solution picks survive a restart.</summary>
    public class PluginSettings
    {
        public PluginSettings()
        {
            SourceSolutions = new List<string>();
            TargetSolutions = new List<string>();
            ShowManaged = true;
        }

        public List<string> SourceSolutions { get; set; }
        public List<string> TargetSolutions { get; set; }
        public bool ShowManaged { get; set; }
        public bool ShowHidden { get; set; }
        public string LastExportFolder { get; set; }
    }
}
