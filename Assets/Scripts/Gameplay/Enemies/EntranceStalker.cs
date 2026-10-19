using System;
using FPLab.Gameplay.Maze;
using FPLab.Gameplay.Session;
using UnityEngine;

namespace FPLab.Gameplay.Enemies
{
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class EntranceStalker : MonoBehaviour
    {
        [Header("Scene")]
        [SerializeField] private MazeBuilder maze;
        [SerializeField] private EscapeSession session;
        [SerializeField] private Transform player;
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private Animator animator;
        [Header("Pursuit")]
        [SerializeField, Min(0f)] private float graceSeconds = 18f;
        [SerializeField, Min(1)] private int minimumSpawnCells = 12;
        [SerializeField, Min(0.1f)] private float movementSpeed = 3.5f;
        [SerializeField, Min(0.1f)] private float repathSeconds = 0.3f;
        [SerializeField, Min(0.1f)] private float catchDistance = 0.95f;
        [SerializeField, Min(0.1f)] private float lightStandOff = 3.2f;
        [Header("Dread")]
        [SerializeField, Min(1f)] private float whisperDistance = 44f;
        [SerializeField, Min(0.1f)] private float closeThreatDistance = 7f;

        private readonly MazePursuitPath path = new MazePursuitPath();
        private readonly MazePursuitPath startPath = new MazePursuitPath();
        private int[] retreatRouteNext;
        private readonly RaycastHit[] sightHits = new RaycastHit[32];
        private CharacterController controller;
        private CharacterController playerController;
        private float graceRemaining, repathRemaining, retreatRemaining, contactSeconds, chaseSeconds;
        private bool retreating;
        private int retreatGoal = -1;
        private int centerCell = -1, waypointCell = -1, targetCell = -1;
        private bool spawned, caught, disabledByCheat;
        private int speedParameter, recoilParameter, attackParameter;
        private bool hasSpeed, hasRecoil, hasAttack;
        private float actualSpeed;

        public float Threat01 { get; private set; }
        public bool PlayerIsSafe { get; private set; }
        public Transform VisualRoot => visualRoot == null ? transform : visualRoot.transform;
        public Vector3 WorldPosition => transform.position;
        public bool IsActive => spawned && session != null && session.State == RunState.Playing;
        public bool IsDisabledByCheat => disabledByCheat;
        public bool IsRetreating => retreating;
        public float RetreatSecondsRemaining => retreatRemaining;
        public int GraphDistanceToPlayer => path.Distance(CellAt(transform.position));
        public event Action Captured;

        // Deliberately survives maze regeneration; a scene restart starts a new run.
        public void DisableForRun()
        {
            disabledByCheat = true;
            Hide();
            PlayerIsSafe = false;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            controller.radius = 0.28f;
            controller.height = 1.8f;
            controller.center = Vector3.up * 0.9f;
            controller.stepOffset = 0.08f;
            controller.skinWidth = 0.025f;
            controller.minMoveDistance = 0f;
            controller.enableOverlapRecovery = true;
            if (player != null) playerController = player.GetComponent<CharacterController>();
            if (animator == null && visualRoot != null) animator = visualRoot.GetComponentInChildren<Animator>();
            if (animator != null) animator.applyRootMotion = false;
            CacheAnimatorParameters();
            Hide();
        }

        private void OnEnable()
        {
            if (maze != null) maze.Generated += ResetRun;
            if (session != null) session.StateChanged += OnStateChanged;
        }

        private void Start()
        {
            ResetRun();
            OnStateChanged();
        }

        private void OnDisable()
        {
            if (maze != null) maze.Generated -= ResetRun;
            if (session != null) session.StateChanged -= OnStateChanged;
            Hide();
            PlayerIsSafe = false;
        }

        private void ResetRun()
        {
            Hide();
            caught = false;
            centerCell = waypointCell = targetCell = -1;
            graceRemaining = graceSeconds;
            repathRemaining = retreatRemaining = contactSeconds = chaseSeconds = 0f;
            retreating = false;
            retreatGoal = -1;
            retreatRouteNext = null;
            PlayerIsSafe = false;
            if (animator != null)
            {
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.Rebind();
                animator.Update(0f);
            }
        }

        private void Hide()
        {
            spawned = false;
            Threat01 = actualSpeed = 0f;
            if (controller != null) controller.enabled = false;
            if (visualRoot != null && visualRoot != gameObject) visualRoot.SetActive(false);
        }

        private void OnStateChanged()
        {
            if (session == null) return;
            if (session.State == RunState.Ready || session.State == RunState.Loading)
            {
                ResetRun();
                return;
            }
            if (animator != null)
                animator.updateMode = session.State == RunState.Caught
                    ? AnimatorUpdateMode.UnscaledTime : AnimatorUpdateMode.Normal;
            if (session.State != RunState.Playing)
            {
                actualSpeed = 0f;
                if (hasSpeed && animator != null) animator.SetFloat(speedParameter, 0f);
            }
        }

        private void Update()
        {
            if (disabledByCheat || session == null || maze == null || maze.Data == null || player == null) return;
            if (session.State != RunState.Playing)
            {
                Threat01 = 0f;
                return;
            }

            Vector3 feet = PlayerFeet();
            LampSafeZone safe = LampSafeZone.FindAt(feet, player);
            PlayerIsSafe = safe != null;
            if (!spawned)
            {
                graceRemaining -= Time.deltaTime;
                if (graceRemaining <= 0f) TrySpawn(feet);
                return;
            }
            repathRemaining -= Time.deltaTime;
            if (repathRemaining <= 0f)
            {
                Repath(feet);
                repathRemaining = repathSeconds;
            }
            if (PlayerIsSafe && !retreating) BeginRetreat();
            if (retreating)
            {
                retreatRemaining = Mathf.Max(0f, retreatRemaining - Time.deltaTime);
                // The retreat timer changes navigation, never player immunity.
                // Following the creature into darkness at close range provokes
                // it immediately; leaving in another direction keeps the gap.
                bool provoked = !PlayerIsSafe
                    && HorizontalDistance(transform.position, feet) <= 3f
                    && HasClearSight(transform.position + Vector3.up, feet + Vector3.up);
                bool finished = !PlayerIsSafe && retreatRemaining <= 0f
                    && HorizontalDistance(transform.position, CellCenter(retreatGoal)) <= 0.05f;
                if (provoked || finished)
                {
                    retreating = false;
                    retreatRemaining = 0f;
                    if (provoked)
                    {
                        Repath(feet);
                        repathRemaining = repathSeconds;
                        // Turn back along the current open edge, rather than
                        // cutting diagonally towards a different corridor.
                        if (path.Distance(centerCell) >= 0
                            && path.Distance(centerCell) <= path.Distance(waypointCell))
                            waypointCell = centerCell;
                        if (hasRecoil && animator != null) animator.ResetTrigger(recoilParameter);
                        Face(feet - transform.position);
                    }
                }
            }
            // Only real lamp protection permits passing the enemy safely.
            IgnorePlayerCollision(PlayerIsSafe);
            UpdateThreat(feet);
            bool contact = !PlayerIsSafe && !caught
                && HorizontalDistance(transform.position, feet) <= catchDistance
                && HasClearSight(transform.position + Vector3.up, feet + Vector3.up);
            contactSeconds = contact ? contactSeconds + Time.deltaTime : 0f;
            if (contactSeconds >= 0.4f)
            {
                Capture();
                return;
            }
            chaseSeconds += Time.deltaTime;

            Vector3 previous = transform.position;
            Move(feet, safe);
            actualSpeed = HorizontalDistance(previous, transform.position) / Mathf.Max(Time.deltaTime, 0.0001f);
            if (hasSpeed && animator != null) animator.SetFloat(speedParameter, actualSpeed, 0.12f, Time.deltaTime);
        }

        private void TrySpawn(Vector3 feet)
        {
            int playerCell = CellAt(feet);
            if (playerCell < 0) return;
            if (PlayerIsSafe) { graceRemaining = 1f; return; }
            path.Rebuild(maze.Data, playerCell);
            startPath.Rebuild(maze.Data, maze.Data.StartY * maze.Data.Width + maze.Data.StartX);
            int progress = startPath.Distance(playerCell);
            if (progress < 10) { graceRemaining = 1f; return; }
            int chosen = -1;
            int count = maze.Data.Width * maze.Data.Height;
            int offset = (int)((uint)maze.GeneratedSeed % (uint)count);
            for (int i = 0; i < count; i++)
            {
                int cell = (i + offset) % count;
                int length = path.Distance(cell), fromStart = startPath.Distance(cell);
                // Spawn on the route already travelled, or a short branch off it.
                // The pursuer must not appear ahead on the only route to the exit.
                if (length < minimumSpawnCells || fromStart < 0 || fromStart >= progress - 4
                    || fromStart + length > progress + 4) continue;
                if (chosen >= 0 && length >= path.Distance(chosen)) continue;
                Vector3 point = CellCenter(cell);
                if (LampSafeZone.FindAt(point, transform) != null) continue;
                if (HasClearSight(point + Vector3.up, feet + Vector3.up)) continue;
                chosen = cell;
            }
            if (chosen < 0) { graceRemaining = 1f; return; }
            controller.enabled = false;
            transform.position = CellCenter(chosen);
            centerCell = waypointCell = chosen;
            targetCell = playerCell;
            if (visualRoot != null && visualRoot != gameObject) visualRoot.SetActive(true);
            controller.enabled = true;
            IgnorePlayerCollision(false);
            spawned = true;
            repathRemaining = 0f;
        }

        private void Repath(Vector3 feet)
        {
            targetCell = CellAt(feet);
            path.Rebuild(maze.Data, targetCell);
        }

        private void UpdateThreat(Vector3 feet)
        {
            int cell = CellAt(transform.position);
            int length = path.Distance(cell);
            if (length < 0) { Threat01 = 0f; return; }
            float distance = length * maze.CellSize;
            if (length == 0) distance = HorizontalDistance(transform.position, feet);
            float target = 1f - Mathf.InverseLerp(closeThreatDistance, whisperDistance, distance);
            Threat01 = Mathf.MoveTowards(Threat01, target, Time.deltaTime * 1.5f);
        }

        private void BeginRetreat()
        {
            int origin = CellAt(transform.position);
            if (origin < 0) return;
            retreating = true;
            retreatRemaining = 10f;
            contactSeconds = 0f;
            IgnorePlayerCollision(true);
            if (hasRecoil && animator != null) animator.SetTrigger(recoilParameter);
            if (HorizontalDistance(transform.position, CellCenter(waypointCell)) <= 0.035f)
                centerCell = waypointCell;
            else if (path.Distance(waypointCell) > path.Distance(centerCell))
                centerCell = waypointCell; // complete the outward half of this edge
            waypointCell = centerCell; // settle on an edge endpoint before turning

            // A room may require briefly moving closer to the player before
            // reaching its doorway. Search all open edges except the occupied
            // safe cell, rather than stopping at a local distance maximum.
            int count = maze.Data.Width * maze.Data.Height;
            var queue = new int[count];
            var depth = new int[count];
            var parent = new int[count];
            for (int i = 0; i < count; i++) depth[i] = -1;
            startPath.Rebuild(maze.Data, maze.Data.StartY * maze.Data.Width + maze.Data.StartX);
            void Search(bool allowSafeCell)
            {
                for (int i = 0; i < count; i++) depth[i] = -1;
                int read = 0, write = 0;
                queue[write++] = centerCell;
                depth[centerCell] = 0;
                retreatGoal = centerCell;
                float bestScore = -1f;
                while (read < write)
                {
                    int cell = queue[read++];
                    int steps = depth[cell];
                    // Prefer a genuinely distant refuge over a long loop near
                    // the player; then favour ten steps and earlier sections.
                    float score = path.Distance(cell) * 1000f
                        + Mathf.Min(steps, 10) * 10f
                        - Mathf.Max(0, startPath.Distance(cell) - startPath.Distance(origin));
                    if (score > bestScore) { bestScore = score; retreatGoal = cell; }
                    if (steps >= 12) continue;
                    for (int direction = 0; direction < 4; direction++)
                    {
                        int next = path.Neighbor(cell, direction);
                        if (next < 0 || depth[next] >= 0 || (!allowSafeCell && next == targetCell)) continue;
                        depth[next] = steps + 1;
                        parent[next] = cell;
                        queue[write++] = next;
                    }
                }
            }
            Search(false);
            // A short dead end behind the lamp may have no other exit. The
            // protected cell is then traversable during retreat, with capture
            // still guarded by actual light and actor collision ignored there.
            if (path.Distance(retreatGoal) < 8) Search(true);
            // Use the exact searched route. Rebuilding an unrestricted shortest
            // path here could otherwise take a shortcut through the safe cell.
            retreatRouteNext = new int[count];
            for (int i = 0; i < count; i++) retreatRouteNext[i] = -1;
            retreatRouteNext[retreatGoal] = retreatGoal;
            int cursor = retreatGoal;
            while (cursor != centerCell)
            {
                int previous = parent[cursor];
                retreatRouteNext[previous] = cursor;
                cursor = previous;
            }
        }

        private int NextRetreatCell(int cell)
        {
            return retreatRouteNext != null && cell >= 0 && cell < retreatRouteNext.Length
                && retreatRouteNext[cell] >= 0 ? retreatRouteNext[cell] : cell;
        }

        private void IgnorePlayerCollision(bool ignore)
        {
            if (controller != null && playerController != null && controller.enabled && playerController.enabled)
                Physics.IgnoreCollision(controller, playerController, ignore);
        }

        private void Move(Vector3 feet, LampSafeZone safe)
        {
            bool reached = HorizontalDistance(transform.position, CellCenter(waypointCell)) <= 0.035f;
            if (reached)
            {
                centerCell = waypointCell;
                waypointCell = retreating ? NextRetreatCell(centerCell) : path.NextTowardsTarget(centerCell);
            }
            if (retreating && reached && centerCell == retreatGoal)
            {
                Face(feet - transform.position);
                return;
            }
            Vector3 destination = CellCenter(waypointCell);
            if (!retreating && centerCell == targetCell && waypointCell == centerCell
                && HasClearSight(transform.position + Vector3.up, feet + Vector3.up))
                destination = new Vector3(feet.x, transform.position.y, feet.z);
            Vector3 delta = destination - transform.position;
            delta.y = 0f;
            float speed = retreating ? 4.2f : movementSpeed;
            // A brief visible rush is escapable by sprinting and cannot make the
            // enemy permanently faster than the player's normal movement.
            if (!retreating && chaseSeconds % 9f < 1f
                && HorizontalDistance(transform.position, feet) > 5f
                && HasClearSight(transform.position + Vector3.up, feet + Vector3.up)) speed = 4.6f;
            Vector3 motion = Vector3.ClampMagnitude(delta, speed * Time.deltaTime);
            if (!retreating && safe != null && NearProtectedLamp(transform.position + motion, safe)
                && HorizontalDistance(transform.position + motion, safe.FloorCenter)
                    <= HorizontalDistance(transform.position, safe.FloorCenter)) return;
            if (motion.sqrMagnitude > 0.000001f)
            {
                controller.Move(motion);
                Face(retreating ? feet - transform.position : delta);
            }
        }

        private bool NearProtectedLamp(Vector3 point, LampSafeZone safe)
        {
            if (safe == null || !safe.IsPowered) return false;
            return safe.Contains(point, transform, controller.radius + 0.15f);
        }

        private void Face(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up), 240f * Time.deltaTime);
        }

        private void Capture()
        {
            caught = true;
            Threat01 = 1f;
            if (animator != null)
            {
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                if (hasSpeed) animator.SetFloat(speedParameter, 0f);
                if (hasAttack) animator.SetTrigger(attackParameter);
            }
            session.CatchPlayer();
            Captured?.Invoke();
        }

        private Vector3 PlayerFeet()
        {
            if (playerController == null) return player.position;
            return player.TransformPoint(playerController.center - Vector3.up * (playerController.height * 0.5f));
        }

        private int CellAt(Vector3 position)
        {
            if (maze == null || maze.Data == null) return -1;
            Vector3 local = maze.transform.InverseTransformPoint(position);
            int x = Mathf.FloorToInt(local.x / maze.CellSize + maze.Data.Width * 0.5f);
            int y = Mathf.FloorToInt(local.z / maze.CellSize + maze.Data.Height * 0.5f);
            return maze.Data.IsWalkable(x, y) ? y * maze.Data.Width + x : -1;
        }

        private Vector3 CellCenter(int cell)
        {
            int x = cell % maze.Data.Width, y = cell / maze.Data.Width;
            return maze.transform.TransformPoint(new Vector3((x - (maze.Data.Width - 1) * 0.5f) * maze.CellSize,
                0f, (y - (maze.Data.Height - 1) * 0.5f) * maze.CellSize));
        }

        private bool HasClearSight(Vector3 origin, Vector3 target)
        {
            Vector3 direction = target - origin;
            int count = Physics.RaycastNonAlloc(origin, direction.normalized, sightHits, direction.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == sightHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Transform hit = sightHits[i].transform;
                if (hit == transform || hit.IsChildOf(transform) || hit == player || hit.IsChildOf(player)) continue;
                return false;
            }
            return true;
        }

        private void CacheAnimatorParameters()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            speedParameter = Animator.StringToHash("Speed");
            recoilParameter = Animator.StringToHash("Recoil");
            attackParameter = Animator.StringToHash("Attack");
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == speedParameter && parameter.type == AnimatorControllerParameterType.Float) hasSpeed = true;
                if (parameter.nameHash == recoilParameter && parameter.type == AnimatorControllerParameterType.Trigger) hasRecoil = true;
                if (parameter.nameHash == attackParameter && parameter.type == AnimatorControllerParameterType.Trigger) hasAttack = true;
            }
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
