using Remedy.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// The base of an Object within the Schematic Framework.
    /// Describes how instances of the Object should interface with the rest of the Schematic Toolkit
    /// </summary>
    [RequireComponent(typeof(ManagerHandshaker))]
    public class SchematicObject : MonoBehaviour
    {
        public SchematicGraph SchematicGraph;
        public SerializableDictionary<GameObject, List<BehaviorMetadata>> Behaviors = new();

        private void OnEnable()
        {
            if(SchematicGraph == null)
            {
                enabled = false;
                return;
            }

            SchematicGraph.ReconstructPortConnections();

            Assign(SchematicGraph);

            foreach (var oninvokeNode in SchematicGraph.FlowOnInvokeCache)
            {
                var isInvoking = false;

                oninvokeNode.Signal.Subscribe(this, () =>
                {
                    if (isInvoking) return;
                    try
                    {
                        isInvoking = true;
                        oninvokeNode.Trigger(gameObject);
                    }
                    finally
                    {
                        isInvoking = false;
                    }
                });

                oninvokeNode.UpdateCaches();
            }
        }

        private void Start()
        {
            SchematicGraph.TriggerEvent<OnCreate>(gameObject);
        }

        private void Update()
        {
            SchematicGraph.TriggerEvent<OnUpdate>(gameObject);
        }

        private void OnDestroy()
        {
            SchematicGraph.TriggerEvent<OnDestroy>(gameObject);
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
            SchematicGraph.TriggerEvent<OnCollisionEnter>(gameObject, collision.collider, collision.impulse, collision.relativeVelocity, collision.articulationBody, collision.transform, collision.gameObject);
        }

        protected virtual void OnCollisionExit(Collision collision)
        {
            SchematicGraph.TriggerEvent<OnCollisionExit>(gameObject, collision.collider, collision.impulse, collision.relativeVelocity, collision.articulationBody, collision.transform, collision.gameObject);
        }
        protected virtual void OnTriggerEnter(Collider collider)
        {
            SchematicGraph.TriggerEvent<OnCollisionEnter>(gameObject, collider);
        }

        protected virtual void OnTriggerExit(Collider collider)
        {
            SchematicGraph.TriggerEvent<OnCollisionExit>(gameObject, collider);
        }

        public void OnValidate()
        {
            // Add OnValidate Event
        }

        public void Assign(SchematicGraph graph)
        {
            this.SchematicGraph = graph;
        }
    }
}