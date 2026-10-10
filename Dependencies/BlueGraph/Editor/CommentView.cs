using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

using UnityEditor.Experimental.GraphView;

namespace BlueGraph.Editor
{
    public class CommentView : GraphElement, ICanDirty
    {
        public Comment Target { get; protected set; }

        private readonly VisualElement titleContainer;
        private readonly TextField titleEditor;
        private readonly Label titleLabel;
        
        private CommentTheme theme;
        private bool isEditingCancelled;

        public CommentView(Comment comment)
        {
            Target = comment;
            SetPosition(comment.Region);
            
            styleSheets.Add(Resources.Load<StyleSheet>("BlueGraphEditor/CommentView"));
            
            titleContainer = new VisualElement();
            titleContainer.AddToClassList("titleContainer");
            
            titleEditor = new TextField();
            
            var input = titleEditor.Q(TextField.textInputUssName);
            input.RegisterCallback<KeyDownEvent>(OnTitleKeyDown);
            input.RegisterCallback<FocusOutEvent>(e => { OnFinishEditingTitle(); });
            
            titleContainer.Add(titleEditor);
            
            titleLabel = new Label();
            titleLabel.text = comment.Text;
            
            titleContainer.Add(titleLabel);

            titleEditor.style.display = DisplayStyle.None;

            Add(titleContainer);

            ClearClassList();
            AddToClassList("commentView");
            
            capabilities |= Capabilities.Selectable | Capabilities.Movable | 
                            Capabilities.Deletable | Capabilities.Resizable;

            RegisterCallback<MouseDownEvent>(OnMouseDown);
            this.AddManipulator(new ContextualMenuManipulator(BuildContextualMenu));
            
            SetTheme(Target.Theme);
        }
        
        public virtual void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is CommentView)
            {
                // Add options to change theme
                foreach (var theme in (CommentTheme[])Enum.GetValues(typeof(CommentTheme)))
                {
                    var actionStatus = DropdownMenuAction.Status.Normal;
                    if (this.theme == theme)
                    {
                        actionStatus = DropdownMenuAction.Status.Disabled;
                    }

                    evt.menu.AppendAction(
                        theme + " Theme", 
                        (a) => { SetTheme(theme); }, 
                        actionStatus
                    );
                }

                evt.menu.AppendSeparator();
            }
        }
        
        /// <summary>
        /// Change the color theme used on the canvas
        /// </summary>
        public void SetTheme(CommentTheme theme)
        {
            RemoveFromClassList("theme-" + this.theme);
            AddToClassList("theme-" + theme);
            this.theme = theme;
            Target.Theme = theme;
        }
        
        private void OnTitleKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    isEditingCancelled = true;
                    OnFinishEditingTitle();
                    break;
                case KeyCode.Return:
                    OnFinishEditingTitle();
                    break;
                default:
                    break;
            }
        }
        
        private void OnFinishEditingTitle()
        {
            // Show the label and hide the editor
            titleLabel.visible = true;
            titleEditor.style.display = DisplayStyle.None;

            if (!isEditingCancelled)
            {
                string oldName = titleLabel.text;
                string newName = titleEditor.value;
                
                titleLabel.text = newName;
                OnRenamed(oldName, newName);
            }
                
            isEditingCancelled = false;
        }
        
        public void EditTitle()
        {
            titleLabel.visible = false;

            titleEditor.SetValueWithoutNotify(Target.Text);
            titleEditor.style.display = DisplayStyle.Flex;
            titleEditor.Q(TextField.textInputUssName).Focus();
        }
        
        public virtual void OnRenamed(string oldName, string newName)
        {
            Target.Text = newName;
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.clickCount == 2)
            {
                if (HitTest(evt.localMousePosition))
                {
                    EditTitle();
                }
            }
        }
        
        /// <summary>
        /// Override HitTest to only trigger when they click the title
        /// </summary>
        public override bool HitTest(Vector2 localPoint)
        {
            Vector2 mappedPoint = this.ChangeCoordinatesTo(titleContainer, localPoint);
            return titleContainer.ContainsPoint(mappedPoint);
        }
        
        public override void SetPosition(Rect newPos)
        {
            var previous = Target.Region;
            base.SetPosition(newPos);
            Target.Region = newPos;

            // A comment groups the nodes inside it: moving it (not resizing) carries them along.
            var delta = newPos.position - previous.position;
            if (delta != Vector2.zero && Mathf.Approximately(newPos.width, previous.width) && Mathf.Approximately(newPos.height, previous.height))
                MoveContainedNodes(previous, delta);
        }

        /// <summary>The nodes lying entirely inside this comment.</summary>
        public List<NodeView> ContainedNodes(Rect region)
        {
            var result = new List<NodeView>();
            var canvas = GetFirstAncestorOfType<CanvasView>();
            if (canvas == null)
                return result;

            foreach (var node in canvas.nodes.ToList())
            {
                if (!(node is NodeView view))
                    continue;
                var rect = view.GetPosition();
                if (region.Contains(rect.min) && region.Contains(rect.max))
                    result.Add(view);
            }
            return result;
        }

        private void MoveContainedNodes(Rect region, Vector2 delta)
        {
            bool moved = false;
            foreach (var view in ContainedNodes(region))
            {
                // Selected nodes are already being dragged along with the comment.
                if (view.selected)
                    continue;
                var rect = view.GetPosition();
                view.SetPosition(new Rect(rect.position + delta, rect.size));
                moved = true;
            }

            var canvas = GetFirstAncestorOfType<CanvasView>();
            if (moved && canvas != null && canvas.Graph != null)
                UnityEditor.EditorUtility.SetDirty(canvas.Graph);
        }

        public void Dirty() { }

        public void Update() { }
    }
}
