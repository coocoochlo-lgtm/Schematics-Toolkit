using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Remedy.Audio;
using Remedy.Schematics.Templates;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Browsers
{
    public class AudioClipBrowserWindow : SchematicBrowserWindow
    {
        private const string AudioRoot = "Assets/Content/Audio";
        private const string MusicRoot = AudioRoot + "/Music";
        private const string SfxRoot = AudioRoot + "/SFX";
        private const string AudioPrefs = "Remedy.AudioClipBrowser";
        private const int WaveformWidth = 512;
        private const int WaveformHeight = 128;

        private readonly List<AudioClip> _clips = new();
        private readonly List<PlayerControls> _players = new();
        private readonly Dictionary<AudioClip, Texture2D> _waveforms = new();
        private readonly HashSet<Texture2D> _ownedWaveforms = new();
        private readonly HashSet<AudioClip> _waveformsPending = new();
        private readonly Dictionary<AudioClip, Label> _nameLabels = new();
        private static Texture2D _glossTexture;
        private string _selectedFolder = AudioRoot;
        private AudioClip _previewClip;
        private int _previewSamplePosition;
        private bool _previewPaused;
        private Slider _tileSizeSlider;
        private ToolbarSearchField _search;
        private ProjectFolderTree _folderTree;
        private VisualElement _page;
        private TagBar _tagBar;
        private const string TagSection = "Audio";
        // Card colours by kind (tags have their own colours, shown as dots).
        private static readonly Color MusicColor = new(0.48f, 0.34f, 0.82f);
        private static readonly Color SfxColor = new(0.15f, 0.55f, 0.47f);
        private VisualElement _clipGrid;
        private Label _status;
        private bool _rebuilding;

        protected override string PrefsKey => AudioPrefs;
        protected override string Title => "Audio Clip Browser";
        protected override string IconName => "AudioClip Icon";
        protected override string Hint => "Click a waveform to preview; drag a clip into the scene or another window.";

        [MenuItem("Window/Remedy/Audio Clip Browser", false, 102)]
        public static void Open() => ProjectManagerWindow.Open(ProjectManagerWindow.Section.Audio);

        protected override void OnEnable()
        {
            base.OnEnable();
            minSize = new Vector2(620f, 320f);
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.update += UpdatePreview;
            EditorApplication.focusChanged += OnEditorFocusChanged;
            SchematicTemplateGrid.SharedTileSizeChanged += OnSharedTileSizeChanged;
            EnsureFolders();
            AssetTags.Changed += OnTagsChanged;
            EditorApplication.delayCall += MigrateOldTags;
            ReloadAssets();
        }

        protected override void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.update -= UpdatePreview;
            EditorApplication.focusChanged -= OnEditorFocusChanged;
            SchematicTemplateGrid.SharedTileSizeChanged -= OnSharedTileSizeChanged;
            AssetTags.Changed -= OnTagsChanged;
            StopPreview();
            foreach (var waveform in _ownedWaveforms)
                DestroyImmediate(waveform);
            _ownedWaveforms.Clear();
            _waveforms.Clear();
            _waveformsPending.Clear();
            base.OnDisable();
        }

        private void OnLostFocus() => StopPreview();

        private void OnTagsChanged() => _page?.schedule.Execute(RebuildCards);

        private bool FilterByTag => EditorPrefs.GetBool(AudioPrefs + ".FilterByTag", true);

        // Audio's earlier tags (Audio Clip Tag assets, one per clip) become shared Project Manager tags: same names and
        // colours, and every clip keeps its tag. The old tag assets go to the system trash afterwards.
        private static void MigrateOldTags()
        {
            var old = AssetDatabase.FindAssets("t:AudioClipTag")
                .Select(guid => (guid, tag: AssetDatabase.LoadAssetAtPath<AudioClipTag>(AssetDatabase.GUIDToAssetPath(guid))))
                .Where(entry => entry.tag != null)
                .ToList();
            if (old.Count == 0)
                return;
            foreach (var (guid, tag) in old)
                AssetTags.Migrate(TagSection, tag.TagName, tag.Color, guid);
            foreach (var (guid, _) in old)
                AssetDatabase.MoveAssetToTrash(AssetDatabase.GUIDToAssetPath(guid));
            Debug.Log($"[Project Manager] Moved {old.Count} audio tag(s) into the shared tags (Assets/Project Manager/Tags/Audio). Clips keep their tags, and can now have several.");
            AssetTags.RaiseChanged();
        }

        private float TileSize => _tileSizeSlider?.value ?? SchematicTemplateGrid.SharedTileSize;

        // The Schematics page (or another Audio page) changed the shared size.
        private void OnSharedTileSizeChanged(float size)
        {
            if (_tileSizeSlider == null)
                return;
            _tileSizeSlider.SetValueWithoutNotify(size);
            RebuildCards();
        }

        // Cards are sized like the Schematics page's tiles (same width and height for the same tile size).
        private static void SizeCard(VisualElement card, float size)
        {
            card.style.width = SchematicTemplateGrid.TileWidth(size);
            card.style.height = SchematicTemplateGrid.TileHeight(size);
            card.style.marginLeft = card.style.marginRight = card.style.marginTop = card.style.marginBottom = SchematicTemplateGrid.TileSpacing;
        }

        private bool MatchesSearch(string name)
        {
            string search = _search?.value?.Trim();
            if (string.IsNullOrEmpty(search))
                return true;
            foreach (var word in search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            return true;
        }

        private void OnEditorFocusChanged(bool focused)
        {
            if (!focused)
                StopPreview();
        }

        // Folders: the grid shows the open folder's subfolders and the clips right in it. Off: every clip in the
        // selected folder and the folders below it.
        private bool FolderView => EditorPrefs.GetBool(AudioPrefs + ".FolderView", true);

        protected override void BuildHeader(VisualElement header)
        {
            header.Add(HeaderToggle("Folders", "On: browse folder by folder. Off: show every clip in the selection at once.", "FolderView", true, _ => RebuildCards()));
            header.Add(HeaderToggle("Filter by Tag", "Clicking a tag shows only the clips that have it.", "FilterByTag", true, on =>
            {
                if (_tagBar != null) _tagBar.FilterEnabled = on;
            }));
            // Importing, folders and tags: the "+" tile, the grid's right-click menu, and the "+ New Tag" chip.
        }

        protected override VisualElement BuildContent()
        {
            _folderTree = new ProjectFolderTree("Audio");
            _folderTree.AddToClassList("audio-browser__folders");
            _folderTree.SetAllRow("All Audio");
            _folderTree.SetGroups(new[]
            {
                new ProjectFolderTree.Group { Key = "Music", Label = "Music", Icon = "AudioClip Icon", Root = MusicRoot, ShowAllSubfolders = true },
                new ProjectFolderTree.Group { Key = "SFX", Label = "SFX", Icon = "AudioSource Icon", Root = SfxRoot, ShowAllSubfolders = true },
            });
            _folderTree.SelectionChanged += () =>
            {
                var group = _folderTree.SelectedGroup;
                _selectedFolder = group == null ? AudioRoot : _folderTree.SelectedFolder ?? group.Root;
                RebuildCards();
            };
            _folderTree.FoldersChanged += ReloadAssets;
            {
                var group = _folderTree.SelectedGroup;
                _selectedFolder = group == null ? AudioRoot : _folderTree.SelectedFolder ?? group.Root;
            }
            var main = new VisualElement();
            main.AddToClassList("audio-browser__main");

            // The sidebar beside the clips, at the width all Project Manager pages share.
            var body = ProjectManagerLayout.Split(_folderTree, main);
            body.AddToClassList("audio-browser");
            _page = body;
            body.RegisterCallback<PointerUpEvent>(OnEditorClick, TrickleDown.TrickleDown);

            // The same top bar as the Schematics page (search and tile size), and the same shared tile size.
            var toolbar = SchematicTemplateGrid.CreateToolbar(out _search, out _tileSizeSlider);
            _search.RegisterValueChangedCallback(_ => RebuildCards());
            _tileSizeSlider.SetValueWithoutNotify(SchematicTemplateGrid.SharedTileSize);
            _tileSizeSlider.RegisterValueChangedCallback(evt => SchematicTemplateGrid.SharedTileSize = evt.newValue);
            main.Add(toolbar);

            _tagBar = new TagBar(TagSection) { FilterEnabled = FilterByTag };
            _tagBar.FilterChanged += RebuildCards;
            main.Add(_tagBar);

            var scroll = new ScrollView();
            scroll.AddToClassList("audio-browser__scroll");
            main.Add(scroll);
            _clipGrid = new VisualElement();
            _clipGrid.AddToClassList("audio-browser__grid");
            scroll.Add(_clipGrid);
            scroll.AddManipulator(new ContextualMenuManipulator(BuildBackgroundMenu));
            // Dropping on the grid moves clips and folders into the open folder.
            ProjectAssetOps.MakeDropTarget(scroll, () => _selectedFolder == AudioRoot ? null : _selectedFolder, _ => ReloadAssets(), "audio-browser__scroll--drop");

            _status = new Label();
            _status.AddToClassList("audio-browser__status");
            main.Add(_status);

            RefreshControls();
            return body;
        }

        protected override void OnPicked(SchematicTemplate item) { }

        private void OnEditorClick(PointerUpEvent evt)
        {
            if (!IsWithin(evt.target as VisualElement, "audio-preview-surface")
                && !IsWithin(evt.target as VisualElement, "audio-player-control"))
                StopPreview();
        }

        private void OnProjectChanged()
        {
            if (_rebuilding)
                return;
            ReloadAssets();
        }

        private void ReloadAssets()
        {
            // Rebuilding would throw away a name being typed: wait for it.
            var busy = _page?.Q(className: "inline-rename");
            if (busy != null)
            {
                _page.schedule.Execute(ReloadAssets).ExecuteLater(300);
                return;
            }
            EnsureFolders();
            _clips.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { MusicRoot, SfxRoot }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null)
                    _clips.Add(clip);
            }
            RebuildAudioData();
            if (rootVisualElement.childCount > 0 && _folderTree != null)
                RefreshControls();
        }

        private void RebuildAudioData()
        {
            var data = AudioData.Instance;
            if (data.RebuildDefinitions(_clips, TypeOf))
                AssetDatabase.SaveAssets();
        }

        private Remedy.Audio.AudioType TypeOf(AudioClip clip)
        {
            var path = AssetDatabase.GetAssetPath(clip);
            return path.StartsWith(MusicRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? Remedy.Audio.AudioType.Music
                : Remedy.Audio.AudioType.SoundEffect;
        }

        private void RefreshControls()
        {
            if (_folderTree == null)
                return;
            _folderTree.Rebuild();
            _tagBar?.Rebuild();
            RebuildCards();
        }

        private void RebuildCards()
        {
            if (_clipGrid == null || _rebuilding)
                return;
            _rebuilding = true;
            try
            {
                _clipGrid.Clear();
                _players.Clear();
                _nameLabels.Clear();
                _clipGrid.Add(BuildImportCard());
                var folders = FolderView ? FoldersInView().Where(f => MatchesSearch(Path.GetFileName(f))).ToList() : new List<string>();
                foreach (var folder in folders)
                    _clipGrid.Add(BuildFolderCard(folder));
                var visible = _clips.Where(IsVisible).ToList();
                foreach (var clip in visible)
                    _clipGrid.Add(BuildCard(clip));
                if (_status != null)
                    _status.text = $"{visible.Count} clip{(visible.Count == 1 ? "" : "s")}"
                        + (folders.Count > 0 ? $", {folders.Count} folder{(folders.Count == 1 ? "" : "s")}" : "");
            }
            finally
            {
                _rebuilding = false;
            }
        }

        // Where imports go: the open folder (SFX when All Audio is open).
        private string ImportTarget => _selectedFolder == AudioRoot ? SfxRoot : SelectedImportRoot();

        // The "+" card, first like the Schematics page's: imports audio files into the open folder.
        private VisualElement BuildImportCard()
        {
            var tile = new ActionTile("Import Audio…", $"Import audio files into {ImportTarget}. Right-click the grid for folders and more.",
                () => EditorApplication.delayCall += ImportFiles);
            tile.Resize(TileSize);
            return tile;
        }

        // Right-clicking the grid's empty space: what the Project window offers there, for audio.
        private void BuildBackgroundMenu(ContextualMenuPopulateEvent evt)
        {
            // Cards have their own menus; this one is only for the space around them.
            if (IsWithin(evt.target as VisualElement, "audio-browser__card"))
                return;
            var menu = evt.menu;
            menu.AppendAction("Import Audio…", _ => ImportFiles());
            menu.AppendAction("Import Folder…", _ => ImportFolder());
            menu.AppendSeparator();
            menu.AppendAction("Create/Folder", _ => CreateFolder());
            menu.AppendAction("Create/Tag…", _ => AssetTagWindow.Open(TagSection, null));
            menu.AppendSeparator();
            string folder = _selectedFolder;
            menu.AppendAction("Show in Project", _ => Ping(AssetDatabase.LoadMainAssetAtPath(folder)));
            menu.AppendAction(SchematicObjectsBrowserWindow.RevealLabel, _ => EditorUtility.RevealInFinder(folder));
            menu.AppendSeparator();
            menu.AppendAction("Refresh", _ =>
            {
                AssetDatabase.Refresh();
                ReloadAssets();
            });
            evt.StopPropagation();
        }

        // The folders shown as cards in the folder view: Music and SFX at the top, else the open folder's subfolders.
        private IEnumerable<string> FoldersInView()
        {
            if (_selectedFolder == AudioRoot)
                return new[] { MusicRoot, SfxRoot };
            return AssetDatabase.IsValidFolder(_selectedFolder)
                ? AssetDatabase.GetSubFolders(_selectedFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                : Enumerable.Empty<string>();
        }

        private static string GroupOf(string path)
            => path.StartsWith(MusicRoot, StringComparison.OrdinalIgnoreCase) ? "Music" : "SFX";

        private void OpenFolder(string folder) => _folderTree?.Select(GroupOf(folder), folder);

        private VisualElement BuildFolderCard(string folder)
        {
            bool fixedFolder = folder == MusicRoot || folder == SfxRoot; // the type roots stay put
            var tile = new FolderTile(folder, locked: fixedFolder)
            {
                Opened = OpenFolder,
                Moved = _ => ReloadAssets(),
                Renamed = ReloadAssets,
                Menu = (menu, f) =>
                {
                    menu.AppendAction("Open", _ => OpenFolder(f));
                    if (!fixedFolder)
                        menu.AppendAction("Rename", _ => (_clipGrid.Query<FolderTile>().Where(t => t.Folder == f).First())?.BeginRename());
                    menu.AppendAction("Show in Folder Tree", _ => _folderTree?.Reveal(GroupOf(f), f));
                    menu.AppendAction("Show in Project", _ => Ping(AssetDatabase.LoadMainAssetAtPath(f)));
                    menu.AppendAction(SchematicObjectsBrowserWindow.RevealLabel, _ => EditorUtility.RevealInFinder(f));
                    if (!fixedFolder)
                    {
                        menu.AppendSeparator();
                        menu.AppendAction("Delete", _ =>
                        {
                            if (ProjectAssetOps.Delete(new[] { f }))
                                ReloadAssets();
                        });
                    }
                },
            };
            tile.Resize(TileSize);
            return tile;
        }

        private bool IsVisible(AudioClip clip)
        {
            if (!MatchesSearch(clip.name) && !GetTags(clip).Any(t => MatchesSearch(t.TagName)))
                return false;
            var path = AssetDatabase.GetAssetPath(clip);
            if (FolderView)
                return _selectedFolder != AudioRoot
                    && string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), _selectedFolder, StringComparison.OrdinalIgnoreCase)
                    && (_tagBar == null || _tagBar.Passes(clip));
            if (_selectedFolder != AudioRoot
                && !path.StartsWith(_selectedFolder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                return false;
            return _tagBar == null || _tagBar.Passes(clip);
        }

        private VisualElement BuildCard(AudioClip clip)
        {
            float size = TileSize;
            var card = new VisualElement();
            card.AddToClassList("audio-browser__card");
            card.AddToClassList("audio-preview-surface");
            SizeCard(card, size);
            card.style.flexDirection = FlexDirection.Column;
            card.style.borderTopColor = card.style.borderRightColor = card.style.borderBottomColor = card.style.borderLeftColor =
                EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.35f) : new Color(0f, 0f, 0f, 0.35f);
            // The card's colour says Music or SFX; the clip's tags show as coloured dots.
            card.style.backgroundColor = TypeOf(clip) == Remedy.Audio.AudioType.Music ? MusicColor : SfxColor;
            var thumbnail = new Image
            {
                image = WaveformFor(clip),
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore
            };
            thumbnail.AddToClassList("audio-browser__waveform");
            card.Add(thumbnail);

            var gloss = new Image
            {
                image = GlossTexture,
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore
            };
            gloss.AddToClassList("audio-browser__gloss");
            card.Add(gloss);

            var flash = new VisualElement { pickingMode = PickingMode.Ignore };
            flash.AddToClassList("audio-browser__flash");

            var playCue = new Label("▶") { pickingMode = PickingMode.Ignore };
            playCue.AddToClassList("audio-browser__play-cue");

            var name = new Label(clip.name) { tooltip = AssetDatabase.GetAssetPath(clip) };
            name.AddToClassList("audio-browser__clip-name");
            name.style.color = TextColorFor(card.style.backgroundColor.value);
            card.Add(name);
            _nameLabels[clip] = name;
            // Double-clicking the name renames the clip (instead of previewing it).
            name.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2)
                    return;
                evt.StopPropagation();
                StopPreview();
                EditorApplication.delayCall += () => BeginRenameClip(clip);
            });

            var tags = GetTags(clip);
            var tagRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            if (tags.Count > 0)
                tagRow.Add(AssetTags.Dots(tags));
            var tagNames = new Label(tags.Count > 0 ? string.Join(", ", tags.Select(t => t.TagName)) : "No tags");
            tagNames.AddToClassList("audio-browser__card-tag");
            tagNames.style.color = TextColorFor(card.style.backgroundColor.value);
            tagNames.style.overflow = Overflow.Hidden;
            tagNames.style.textOverflow = TextOverflow.Ellipsis;
            tagNames.style.whiteSpace = WhiteSpace.NoWrap;
            tagNames.style.flexShrink = 1;
            tagRow.Add(tagNames);
            tagRow.tooltip = tags.Count > 0 ? string.Join(", ", tags.Select(t => t.TagName)) : "No tags: drag a tag onto the clip";
            card.Add(tagRow);

            // "This is audio": a small speaker in the corner (Audio Graphs will show something of their own).
            var speaker = new Image
            {
                image = EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_AudioSource Icon" : "AudioSource Icon")?.image,
                pickingMode = PickingMode.Ignore,
                tooltip = "Audio clip",
            };
            speaker.AddToClassList("audio-browser__kind-icon");
            card.Add(speaker);

            var timeline = BuildTimeline(clip);
            timeline.Container.style.position = Position.Absolute;
            timeline.Container.style.left = 7f;
            timeline.Container.style.right = 7f;
            timeline.Container.style.bottom = 5f;
            card.Add(timeline.Container);
            card.Add(flash);
            card.Add(playCue);

            card.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || IsWithin(evt.target as VisualElement, "audio-player-control"))
                    return;
                timeline.PressPosition = evt.position;
                timeline.PressTime = EditorApplication.timeSinceStartup;
                timeline.Pressed = true;
            });
            var baseBorder = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.35f) : new Color(0f, 0f, 0f, 0.35f);
            card.RegisterCallback<PointerEnterEvent>(_ =>
            {
                var highlight = EditorGUIUtility.isProSkin ? new Color(0.65f, 0.82f, 1f, 0.9f) : new Color(0.12f, 0.38f, 0.7f, 0.85f);
                card.style.borderTopColor = card.style.borderRightColor = card.style.borderBottomColor = card.style.borderLeftColor = highlight;
            });
            card.RegisterCallback<PointerLeaveEvent>(_ =>
                card.style.borderTopColor = card.style.borderRightColor = card.style.borderBottomColor = card.style.borderLeftColor = baseBorder);
            card.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!timeline.Pressed || (evt.pressedButtons & 1) == 0)
                    return;
                if (((Vector2)evt.position - timeline.PressPosition).sqrMagnitude < 36f)
                    return;

                timeline.Pressed = false;
                timeline.Dragged = true;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new UnityEngine.Object[] { clip };
                DragAndDrop.paths = new[] { AssetDatabase.GetAssetPath(clip) };
                DragAndDrop.StartDrag(clip.name);
                evt.StopPropagation();
            });
            card.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!timeline.Pressed)
                {
                    timeline.Pressed = false;
                    timeline.Dragged = false;
                    return;
                }
                timeline.Pressed = false;
                if (evt.button == 0 && !IsWithin(evt.target as VisualElement, "audio-player-control"))
                    FlashCard(card);
                if (evt.button == 0
                    && !timeline.Dragged
                    && !IsWithin(evt.target as VisualElement, "audio-player-control")
                    && EditorApplication.timeSinceStartup - timeline.PressTime <= 0.45d)
                    TogglePreview(clip);
                timeline.Dragged = false;
            });
            card.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (timeline.Pressed)
                    timeline.Dragged = true;
            });

            timeline.PlayCue = playCue;
            _players.Add(timeline);
            card.RegisterCallback<ContextClickEvent>(evt =>
            {
                ShowClipMenu(clip);
                evt.StopPropagation(); // not the grid's own menu as well
            });
            AssetTags.MakeDropTarget(card, () => clip, "audio-browser__card--drop");
            return card;
        }

        private PlayerControls BuildTimeline(AudioClip clip)
        {
            var container = new VisualElement();
            container.AddToClassList("audio-browser__timeline");
            container.AddToClassList("audio-player-control");
            var time = new Label($"0:00 / {FormatTime(clip.length)}");
            time.AddToClassList("audio-browser__time");
            container.Add(time);

            var seek = new Slider(0f, Mathf.Max(clip.samples - 1, 1))
            {
                value = 0f,
                showInputField = false
            };
            seek.AddToClassList("audio-browser__timeline-slider");
            seek.AddToClassList("audio-player-control");
            seek.RegisterValueChangedCallback(evt =>
            {
                if (_previewClip == clip)
                {
                    _previewSamplePosition = Mathf.RoundToInt(evt.newValue);
                    if (!_previewPaused)
                        AudioPreview.SetSamplePosition(clip, _previewSamplePosition);
                    else
                        UpdatePreviewTime(clip, _previewSamplePosition);
                }
            });
            seek.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (_previewClip != clip)
                    StopPreview();
            });
            container.Add(seek);
            return new PlayerControls(clip, container, seek, time);
        }

        private void TogglePreview(AudioClip clip)
        {
            if (_previewClip == clip)
            {
                if (_previewPaused)
                {
                    AudioPreview.Play(clip, _previewSamplePosition);
                    _previewPaused = false;
                }
                else
                {
                    _previewSamplePosition = Mathf.Clamp(
                        Mathf.RoundToInt(AudioPreview.Position() * clip.frequency),
                        0,
                        Mathf.Max(clip.samples - 1, 0));
                    AudioPreview.Stop();
                    _previewPaused = true;
                    UpdatePreviewTime(clip, _previewSamplePosition);
                }
                return;
            }
            StopPreview();
            _previewSamplePosition = 0;
            _previewPaused = false;
            AudioPreview.Play(clip, _previewSamplePosition);
            _previewClip = clip;
            var activePlayer = _players.FirstOrDefault(player => player.Clip == clip);
            if (activePlayer != null)
            {
                activePlayer.PlayCue.AddToClassList("audio-browser__play-cue--visible");
                activePlayer.PlayCue.schedule.Execute(() =>
                {
                    if (activePlayer.PlayCue != null)
                        activePlayer.PlayCue.RemoveFromClassList("audio-browser__play-cue--visible");
                }).ExecuteLater(650);
            }
            UpdatePlayers();
        }

        private static void FlashCard(VisualElement card)
        {
            card.RemoveFromClassList("audio-browser__card--flash");
            card.schedule.Execute(() => card.AddToClassList("audio-browser__card--flash")).ExecuteLater(1);
            card.schedule.Execute(() => card.RemoveFromClassList("audio-browser__card--flash")).ExecuteLater(420);
        }

        private void StopPreview()
        {
            if (_previewClip == null)
                return;
            AudioPreview.Stop();
            _previewClip = null;
            _previewSamplePosition = 0;
            _previewPaused = false;
            UpdatePlayers();
        }

        internal void StopHostedPreview() => StopPreview();

        private void UpdatePlayers()
        {
            foreach (var player in _players)
            {
                if (player.Clip != _previewClip)
                {
                    player.Seek.SetValueWithoutNotify(0f);
                    player.Time.text = $"0:00 / {FormatTime(player.Clip.length)}";
                }
            }
        }

        private void UpdatePreview()
        {
            RefreshPendingWaveforms();
            if (_previewClip == null)
                return;
            if (_previewPaused)
                return;
            if (!AudioPreview.IsPlaying())
            {
                StopPreview();
                return;
            }
            var player = _players.FirstOrDefault(item => item.Clip == _previewClip);
            if (player == null)
                return;
            float seconds = AudioPreview.Position();
            _previewSamplePosition = Mathf.Clamp(Mathf.RoundToInt(seconds * _previewClip.frequency), 0, Mathf.Max(_previewClip.samples - 1, 0));
            UpdatePreviewTime(_previewClip, _previewSamplePosition);
        }

        private void UpdatePreviewTime(AudioClip clip, int samplePosition)
        {
            var player = _players.FirstOrDefault(item => item.Clip == clip);
            if (player == null)
                return;
            player.Seek.SetValueWithoutNotify(samplePosition);
            player.Time.text = FormatTime(samplePosition / (float)clip.frequency) + " / " + FormatTime(clip.length);
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        private static Color TextColorFor(Color color)
        {
            float luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            return luminance > 0.55f ? Color.black : Color.white;
        }

        private static Texture2D GlossTexture
        {
            get
            {
                if (_glossTexture == null)
                    _glossTexture = Resources.Load<Texture2D>("Textures/button-gradient");
                return _glossTexture;
            }
        }

        private Texture2D WaveformFor(AudioClip clip)
        {
            if (_waveforms.TryGetValue(clip, out var waveform) && waveform != null)
                return waveform;

            waveform = AudioPreview.Waveform(clip);
            if (waveform != null)
            {
                _waveforms[clip] = waveform;
                return waveform;
            }

            waveform = new Texture2D(WaveformWidth, WaveformHeight, TextureFormat.RGBA32, false)
            {
                name = $"{clip.name} Waveform",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[WaveformWidth * WaveformHeight];
            var amplitudes = ReadWaveformAmplitudes(clip);
            for (int x = 0; x < WaveformWidth; x++)
            {
                float amplitude = amplitudes != null
                    ? amplitudes[x]
                    : 0.035f;
                int halfHeight = Mathf.Clamp(Mathf.CeilToInt(amplitude * (WaveformHeight * 0.46f)), 1, WaveformHeight / 2);
                for (int y = WaveformHeight / 2 - halfHeight; y <= WaveformHeight / 2 + halfHeight; y++)
                    pixels[y * WaveformWidth + x] = new Color(1f, 1f, 1f, amplitudes == null ? 0.48f : 0.9f);
            }
            waveform.SetPixels(pixels);
            waveform.Apply(false, false);
            _waveforms[clip] = waveform;
            _ownedWaveforms.Add(waveform);
            if (amplitudes == null && clip.loadState == AudioDataLoadState.Loading)
                _waveformsPending.Add(clip);
            return waveform;
        }

        private float[] ReadWaveformAmplitudes(AudioClip clip)
        {
            if (clip == null || clip.samples <= 0 || clip.channels <= 0)
                return null;

            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            int framesPerBucket = Mathf.Max(1, Mathf.CeilToInt(clip.samples / (float)WaveformWidth));
            var amplitudes = new float[WaveformWidth];
            const int bucketsPerRead = 32;
            for (int firstBucket = 0; firstBucket < WaveformWidth; firstBucket += bucketsPerRead)
            {
                int firstFrame = firstBucket * framesPerBucket;
                if (firstFrame >= clip.samples)
                    break;
                int bucketCount = Mathf.Min(bucketsPerRead, WaveformWidth - firstBucket);
                int frameCount = Mathf.Min(bucketCount * framesPerBucket, clip.samples - firstFrame);
                var data = new float[frameCount * clip.channels];
                if (!clip.GetData(data, firstFrame))
                    return null;

                for (int bucket = 0; bucket < bucketCount; bucket++)
                {
                    int bucketFirstFrame = bucket * framesPerBucket;
                    if (bucketFirstFrame >= frameCount)
                        break;
                    int bucketFrames = Mathf.Min(framesPerBucket, frameCount - bucketFirstFrame);
                    float peak = 0f;
                    for (int frame = 0; frame < bucketFrames; frame++)
                        for (int channel = 0; channel < clip.channels; channel++)
                            peak = Mathf.Max(peak, Mathf.Abs(data[(bucketFirstFrame + frame) * clip.channels + channel]));
                    amplitudes[firstBucket + bucket] = peak;
                }
            }
            return amplitudes;
        }

        private void RefreshPendingWaveforms()
        {
            if (_waveformsPending.Count == 0)
                return;
            foreach (var clip in _waveformsPending.ToArray())
            {
                if (clip == null || clip.loadState == AudioDataLoadState.Failed)
                {
                    _waveformsPending.Remove(clip);
                    continue;
                }
                if (clip.loadState != AudioDataLoadState.Loaded)
                    continue;

                _waveformsPending.Remove(clip);
                var amplitudes = ReadWaveformAmplitudes(clip);
                if (amplitudes == null || !_waveforms.TryGetValue(clip, out var waveform) || waveform == null)
                    continue;
                var pixels = new Color[WaveformWidth * WaveformHeight];
                for (int x = 0; x < WaveformWidth; x++)
                {
                    int halfHeight = Mathf.Clamp(Mathf.CeilToInt(amplitudes[x] * (WaveformHeight * 0.46f)), 1, WaveformHeight / 2);
                    for (int y = WaveformHeight / 2 - halfHeight; y <= WaveformHeight / 2 + halfHeight; y++)
                        pixels[y * WaveformWidth + x] = new Color(1f, 1f, 1f, 0.9f);
                }
                waveform.SetPixels(pixels);
                waveform.Apply(false, false);
            }
        }

        private static bool IsWithin(VisualElement element, string className)
        {
            while (element != null)
            {
                if (element.ClassListContains(className))
                    return true;
                element = element.parent;
            }
            return false;
        }

        private static List<AssetTag> GetTags(AudioClip clip) => AssetTags.Of(clip, TagSection);

        private void ShowClipMenu(AudioClip clip)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Select in Project"), false, () => Ping(clip));
            menu.AddItem(new GUIContent(SchematicObjectsBrowserWindow.RevealLabel), false, () => EditorUtility.RevealInFinder(AssetDatabase.GetAssetPath(clip)));
            menu.AddItem(new GUIContent("Rename"), false, () => BeginRenameClip(clip));
            menu.AddItem(new GUIContent("Show in Folder Tree"), false, () =>
            {
                string path = AssetDatabase.GetAssetPath(clip);
                _folderTree?.Reveal(GroupOf(path), path);
            });
            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                if (ProjectAssetOps.Delete(new[] { AssetDatabase.GetAssetPath(clip) }))
                    ReloadAssets();
            });
            menu.AddSeparator("");
            foreach (var tag in AssetTags.In(TagSection))
            {
                var t = tag;
                menu.AddItem(new GUIContent("Tags/" + t.TagName), AssetTags.Has(clip, t), () => AssetTags.Toggle(clip, t));
            }
            menu.AddSeparator("");
            AddMoveMenus(menu, clip, MusicRoot);
            AddMoveMenus(menu, clip, SfxRoot);
            menu.ShowAsContext();
        }

        // Swaps the clip's name for a text field; Enter (or clicking away) renames the file, Escape cancels.
        private void BeginRenameClip(AudioClip clip)
        {
            if (clip == null || !_nameLabels.TryGetValue(clip, out var label) || label.parent == null)
                return;
            var field = InlineRename.Begin(label, clip.name, value =>
            {
                if (InlineRename.RenameAsset(AssetDatabase.GetAssetPath(clip), SanitizeName(value)))
                    label.text = value;
            });
            // Clicks in the field don't preview or stop the preview.
            field?.AddToClassList("audio-player-control");
            field?.AddToClassList("audio-browser__rename");
        }

        private void AddMoveMenus(GenericMenu menu, AudioClip clip, string root)
        {
            AddMoveMenuItem(menu, clip, root, Path.GetFileName(root), true);
            AddChildMoveMenus(menu, clip, root, root, Path.GetFileName(root));
        }

        private void AddChildMoveMenus(GenericMenu menu, AudioClip clip, string root, string parent, string label)
        {
            foreach (var folder in AssetDatabase.GetSubFolders(parent).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string childLabel = label + "/" + folder.Substring(root.Length + 1);
                AddMoveMenuItem(menu, clip, folder, childLabel, false);
                AddChildMoveMenus(menu, clip, root, folder, label);
            }
        }

        private void AddMoveMenuItem(GenericMenu menu, AudioClip clip, string folder, string label, bool preserveFolders)
        {
            string source = AssetDatabase.GetAssetPath(clip);
            string sourceFolder = Path.GetDirectoryName(source)?.Replace("\\", "/");
            if (string.Equals(sourceFolder, folder, StringComparison.OrdinalIgnoreCase))
                menu.AddDisabledItem(new GUIContent("Move To/" + label));
            else
                menu.AddItem(new GUIContent("Move To/" + label), false, () =>
                {
                    MoveClip(clip, folder, preserveFolders);
                    ReloadAssets();
                });
        }

        private void CreateFolder()
        {
            string parent = _selectedFolder == AudioRoot ? SfxRoot : _selectedFolder;
            var group = new ProjectFolderTree.Group { Key = GroupOf(parent), Root = GroupOf(parent) == "Music" ? MusicRoot : SfxRoot };
            _folderTree?.CreateFolder(group, parent);
        }

        private void ImportFiles()
        {
            var files = EditorUtility.OpenFilePanelWithFilters("Import Audio", "", new[] { "Audio files", "wav,mp3,ogg,aif,aiff,mod,it,s3m,xm", "All files", "*" });
            if (!string.IsNullOrEmpty(files))
                ImportPaths(new[] { files }, SelectedImportRoot());
        }

        private void ImportFolder()
        {
            string folder = EditorUtility.OpenFolderPanel("Import Audio Folder", "", "");
            if (!string.IsNullOrEmpty(folder))
                ImportPaths(new[] { folder }, SelectedImportRoot());
        }

        private string SelectedImportRoot()
        {
            if (_selectedFolder.StartsWith(MusicRoot, StringComparison.OrdinalIgnoreCase)
                || _selectedFolder.StartsWith(SfxRoot, StringComparison.OrdinalIgnoreCase))
                return _selectedFolder;
            return SfxRoot;
        }

        private void ImportPaths(IEnumerable<string> paths, string root)
        {
            EnsureFolders();
            foreach (var source in paths)
            {
                if (Directory.Exists(source))
                {
                    string folderName = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    ImportDirectory(source, root + "/" + SanitizeName(folderName));
                }
                else if (File.Exists(source))
                {
                    if (IsAudioFile(source))
                        CopyAudioFile(source, root);
                    else
                        Debug.LogWarning($"Skipped unsupported audio file '{source}'.");
                }
            }
            AssetDatabase.Refresh();
            ReloadAssets();
        }

        private void ImportDirectory(string source, string destination)
        {
            CreateAssetFolder(destination);
            foreach (var file in Directory.GetFiles(source))
                if (IsAudioFile(file))
                    CopyAudioFile(file, destination);
            foreach (var child in Directory.GetDirectories(source))
                ImportDirectory(child, destination + "/" + SanitizeName(Path.GetFileName(child)));
        }

        private void CopyAudioFile(string source, string destination)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(destination + "/" + SanitizeName(Path.GetFileName(source)));
            try
            {
                File.Copy(source, path);
            }
            catch (IOException exception)
            {
                Debug.LogError($"Could not import audio file '{source}': {exception.Message}");
            }
        }

        private static bool IsAudioFile(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".wav":
                case ".mp3":
                case ".ogg":
                case ".aif":
                case ".aiff":
                case ".mod":
                case ".it":
                case ".s3m":
                case ".xm":
                    return true;
                default:
                    return false;
            }
        }

        private static string SanitizeName(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name;
        }

        private static void MoveClip(AudioClip clip, string destinationRoot, bool preserveFolders = true)
        {
            string source = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(source))
                return;
            string currentRoot = source.StartsWith(MusicRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? MusicRoot
                : source.StartsWith(SfxRoot + "/", StringComparison.OrdinalIgnoreCase) ? SfxRoot : null;
            string relative = currentRoot == null ? Path.GetFileName(source) : source.Substring(currentRoot.Length + 1);
            string relativeFolder = preserveFolders && currentRoot != null
                ? Path.GetDirectoryName(relative)?.Replace("\\", "/") ?? ""
                : "";
            string destinationFolder = string.IsNullOrEmpty(relativeFolder)
                ? destinationRoot
                : destinationRoot + "/" + relativeFolder;
            CreateAssetFolder(destinationFolder);
            string destination = destinationFolder.TrimEnd('/') + "/" + Path.GetFileName(relative);
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                return;
            destination = AssetDatabase.GenerateUniqueAssetPath(destination);
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error))
                Debug.LogError($"Could not move audio clip '{source}' to '{destination}': {error}");
        }

        private static void EnsureFolders()
        {
            CreateAssetFolder(AudioRoot);
            CreateAssetFolder(MusicRoot);
            CreateAssetFolder(SfxRoot);
        }

        private static void CreateAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(parent))
                return;
            CreateAssetFolder(parent);
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, name)))
                Debug.LogError($"Could not create project folder '{path}'.");
        }

        private sealed class PlayerControls
        {
            public readonly AudioClip Clip;
            public readonly VisualElement Container;
            public readonly Slider Seek;
            public readonly Label Time;
            public Label PlayCue;
            public Vector2 PressPosition;
            public double PressTime;
            public bool Pressed;
            public bool Dragged;

            public PlayerControls(AudioClip clip, VisualElement container, Slider seek, Label time)
            {
                Clip = clip;
                Container = container;
                Seek = seek;
                Time = time;
            }
        }

        private static class AudioPreview
        {
            private static readonly Type UtilityType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

            public static Texture2D Waveform(AudioClip clip)
            {
                if (UtilityType == null)
                    return null;
                var method = UtilityType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(candidate =>
                    {
                        if (candidate.Name != "GetWaveForm" || candidate.ReturnType != typeof(Texture2D))
                            return false;
                        var parameters = candidate.GetParameters();
                        return parameters.Length == 4
                            && parameters[0].ParameterType == typeof(AudioClip)
                            && parameters[1].ParameterType == typeof(Rect)
                            && parameters[2].ParameterType == typeof(float)
                            && parameters[3].ParameterType == typeof(float);
                    });
                if (method == null)
                    return null;
                try
                {
                    return method.Invoke(null, new object[] { clip, new Rect(0f, 0f, WaveformWidth, WaveformHeight), 1f, 0f }) as Texture2D;
                }
                catch (TargetInvocationException exception)
                {
                    Debug.LogException(exception.InnerException ?? exception);
                    return null;
                }
            }

            private static object Call(string name, params object[] arguments)
            {
                if (UtilityType == null)
                    throw new InvalidOperationException("Unity's AudioUtil preview API is unavailable.");
                var method = UtilityType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length);
                if (method == null)
                    throw new MissingMethodException(UtilityType.FullName, name);
                try
                {
                    return method.Invoke(null, arguments);
                }
                catch (TargetInvocationException exception)
                {
                    Debug.LogException(exception.InnerException ?? exception);
                    return null;
                }
            }

            public static void Play(AudioClip clip, int startSample = 0) => Call("PlayPreviewClip", clip, startSample, false);
            public static void Stop() => Call("StopAllPreviewClips");
            public static bool IsPlaying() => Call("IsPreviewClipPlaying") is bool playing && playing;
            public static float Position() => Call("GetPreviewClipPosition") is float position ? position : 0f;
            public static void SetSamplePosition(AudioClip clip, int sample) => Call("SetPreviewClipSamplePosition", clip, sample);
        }
    }
}
