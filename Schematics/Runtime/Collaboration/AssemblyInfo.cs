using System.Runtime.CompilerServices;

// The wire protocol types (CollabProtocol.cs) and the transport hooks are internal: the editor bridge, the editor's
// collaboration features and the build steps use them, game code goes through CollabRuntime.
[assembly: InternalsVisibleTo("Remedy.Schematics.Collaboration")]
[assembly: InternalsVisibleTo("Remedy.Schematics.Collaboration.Features")]
