// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using UnityEngine;

[SchematicDoc(@"{""name"": ""On Before Splash Screen"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""In a singleton graph: starts a flow before the splash screen, as the game boots."", ""use_for"": [""very early setup""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
[Node(Path = "Events"), Tags("Runtime")]
public class OnBeforeSplashScreen : SchematicEventNode
{ }