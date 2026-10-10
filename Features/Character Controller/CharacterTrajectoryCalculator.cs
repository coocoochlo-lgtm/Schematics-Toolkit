using Remedy.Framework;
using UnityEngine;
using System.Collections.Generic;

namespace Remedy.CharacterControllers
{
    // TODO: Try to remove the helper functions for height changes, they may be causing the slight innaccuracies.
    // Trajectories render correctly for a split second when jumping 

    /// <summary>
    /// Calculates trajectory predictions for the PhysicsBasedCharacterController
    /// </summary>
    [SelectionBase]
    public class CharacterTrajectoryCalculator : Singleton<CharacterTrajectoryCalculator>
    {
        /// <summary>
        /// The character to simulate. Found automatically if left empty.
        /// </summary>
        public GameObject controller;
        public float Contrast = 3f;

        // The first spawned object with a Character Controller.
        private static GameObject FindCharacter()
        {
            var runtime = ExecutionContext.GetRuntime<PhysicsBasedCharacterController>();
            return runtime != null && runtime.Count > 0 ? runtime[0].GameObject : null;
        }

        /// <summary>
        /// Calculates the predicted trajectory points for a given initial velocity
        /// </summary>
        /// <param name="controller">The character controller to simulate</param>
        /// <param name="initialVelocity">The velocity to set at the start of simulation</param>
        /// <param name="timeStep">Physics simulation time step (usually Time.fixedDeltaTime)</param>
        /// <param name="maxTime">Maximum time to simulate</param>
        /// <param name="maxPoints">Maximum number of trajectory points to return</param>
        /// <returns>Array of predicted positions</returns>
        public static Vector3[] CalculateTrajectory(
            Vector3 startingPosition,
            Vector3 initialVelocity,
            float timeStep = 0.02f,
            float maxTime = 5f,
            int maxPoints = 100,
            bool includeHangtime = true)
        {
            List<Vector3> trajectoryPoints = new List<Vector3>();

            // Get initial state
            Vector3 position = startingPosition;
            Vector3 velocity = initialVelocity;
            if (Instance.controller == null)
                Instance.controller = FindCharacter();
            if (Instance.controller == null ||
                !PhysicsBasedCharacterController.TryGetProperties(Instance.controller, out var properties))
                return trajectoryPoints.ToArray();

            var raycast = Instance.controller.GetSchematicBehavior<CharacterRaycastContext>();
            var raycastProperties = raycast != null ? raycast.Properties.Value : null;
            if (raycastProperties == null)
                return trajectoryPoints.ToArray();

            Rigidbody rb = Instance.controller.GetComponent<Rigidbody>();
            float mass = rb.mass;

            // Simulation parameters from controller

            Vector3 baseGravity = (Physics.gravity * mass * 2f) + (Physics.gravity * mass * ((properties.FallSpeed + 1f))) ;
            Vector3 gravity = baseGravity;
            LayerMask terrainLayer = raycastProperties.CollisionMask;

            trajectoryPoints.Add(position);

            float currentTime = 0f;
            int pointCount = 0;
            float simulatedHangTime = 0f;

            while (currentTime < maxTime && pointCount < maxPoints)
            {

                // Check if we hit ground
                (bool hitGround, RaycastHit groundHit) = SimulateGroundRaycast(
                    position,
                    2f,
                    terrainLayer);

                Vector3 totalForce = Vector3.zero;

                // Apply gravity (doubled like in the controller)
                totalForce += gravity;



                Vector3 acceleration = totalForce;
                velocity += acceleration * timeStep;
                // Apply wall collision forces if moving fast enough
                if (velocity.magnitude > 0.1f)
                {
                    Vector3 wallForce = CalculateWallCollisionForce(position, velocity);
                    if (wallForce != Vector3.zero)
                        velocity = wallForce;
                }

                float yVel = velocity.y;
                velocity = Vector3.Lerp(velocity, Vector3.zero, timeStep);
                velocity.y = yVel;

                position += velocity * timeStep;

                if (velocity.y <= 0)
                {
                    if (simulatedHangTime > properties.HangTimeDuration || !includeHangtime)
                    {
                        // If we're very close to ground, we might be landing
                        if (hitGround && groundHit.distance <= properties.RideHeight * 1.3f)
                        {
                            // Character has landed, trajectory ends here
                            trajectoryPoints.Add(groundHit.point + Vector3.up * properties.RideHeight);
                            break;
                        }

                        gravity.y += baseGravity.y * timeStep;
                    }
                    else
                    {
                        gravity.y = 0;
                        simulatedHangTime += timeStep;
                    }
                }
                else
                {
                    gravity.y += baseGravity.y * timeStep;
                }

                trajectoryPoints.Add(position);

                currentTime += timeStep;
                pointCount++;
            }

            return trajectoryPoints.ToArray();
        }

        /// <summary>
        /// Simulates the ground raycast from the character controller
        /// </summary>
        private static (bool, RaycastHit) SimulateGroundRaycast(
            Vector3 position,
            float rayLength,
            LayerMask terrainLayer)
        {
            RaycastHit hit;
            Ray ray = new Ray(position, Vector3.down);
            bool hitGround = Physics.SphereCast(ray, 0.5f, out hit, rayLength, terrainLayer);

            if (!hitGround)
            {
                hitGround = Physics.SphereCast(ray, 0.15f, out hit, rayLength, terrainLayer);
            }

            return (hitGround, hit);
        }


        /// <summary>
        /// Calculates potential wall collision forces
        /// </summary>
        private static Vector3 CalculateWallCollisionForce(
            Vector3 position,
            Vector3 velocity)
        {
            Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
            Vector3 movementDirection = horizontalVelocity.normalized;

            var raycast = Instance.controller != null ? Instance.controller.GetSchematicBehavior<CharacterRaycastContext>() : null;
            var raycastProperties = raycast != null ? raycast.Properties.Value : null;
            LayerMask wallLayer = raycastProperties != null ? raycastProperties.CollisionMask : (LayerMask)Physics.DefaultRaycastLayers;
            float wallSlideDistance = 2;

            RaycastHit hit;
            bool hitWall = Physics.SphereCast(
                position,
                0.1f,
                movementDirection,
                out hit,
                wallSlideDistance,
                wallLayer
            );

            if (hitWall)
            {
                Vector3 wallNormal = hit.normal;

                Vector3 newVel = Vector3.ProjectOnPlane(velocity, wallNormal);

                return newVel;
            }

            return Vector3.zero;
        }
    }
}