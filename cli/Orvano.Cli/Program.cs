using System.CommandLine;

// `orvano --version` prints the product version from Directory.Build.props.
// Commands arrive with feature 36 onwards.
var root = new RootCommand("Orvano command line interface");

return root.Parse(args).Invoke();
