using System;
using System.Collections.Generic;
using UnityEngine;
namespace Ohajiki
{
    public enum SpecialCharge { None, AttackBoost, SpeedBoost, Split, VerticalMissile, HorizontalMissile, Warp }

    public sealed class ShotSimulation
    {
        public sealed class Charge
        {
            public Vector2 Position;
            public Element Element;
            public SpecialCharge Special;
            public bool Active = true;
        }
        public sealed class Projectile
        {
            public Vector2 Position { get; internal set; }
            public Vector2 Velocity { get; internal set; }
            public bool Active { get; internal set; }
            public bool IsClone { get; internal set; }
            internal float AttackMultiplier = 1;
        }
        public sealed class Trap
        {
            public Vector2 Position { get; internal set; }
            public bool EnemyOwner { get; internal set; }
            public Element TargetElement { get; internal set; }
            public int Damage { get; internal set; }
            public int OpponentTurns { get; internal set; }
        }
        public const float Width = 624, Height = 650;
        public readonly Charge[] Charges;
        public Vector2 Position => primary.Position;
        public Vector2 Velocity => primary.Velocity;
        public int Matched { get; private set; }
        public int Collected { get; private set; }
        public float WeightedContacts { get; private set; }
        public double DamageWeight { get; private set; }
        public int ResetCount { get; private set; }
        public int AllClearCount { get; private set; }
        public bool AwaitingAllClearRefill => allClearAt >= 0;
        public float AllClearWaitSeconds => AwaitingAllClearRefill ? Mathf.Max(0, elapsed - allClearAt) : 0;
        public bool Moving { get; private set; }
        public bool EnemySide { get; private set; }
        public IReadOnlyList<Projectile> Projectiles => projectileView;
        public IReadOnlyList<Trap> Traps => trapView;
        public event Action<int, bool> CollectedCharge;
        public event Action<int, SpecialCharge> SpecialTriggered;
        public event Action AllCleared;
        // Owner side and raw damage. The battle owns HP and mitigation.
        public event Action<bool, int> TrapTriggered;
        readonly List<Projectile> projectiles = new List<Projectile>();
        readonly List<Trap> traps = new List<Trap>();
        readonly IReadOnlyList<Projectile> projectileView;
        readonly IReadOnlyList<Trap> trapView;
        readonly Projectile primary = new Projectile();
        readonly BattleTuning tuning;
        readonly System.Random random;
        SpiritDefinition actingSpirit;
        Element shotElement;
        float elapsed;
        float allClearAt = -1;
        bool actionSplitUsed;
        Vector2 playerLaunchPosition, enemyLaunchPosition;

        public ShotSimulation(BattleTuning tuning)
        {
            this.tuning = tuning;
            random = new System.Random(tuning.seed);
            playerLaunchPosition = new Vector2(Width / 2, Height * tuning.launchInset);
            enemyLaunchPosition = new Vector2(Width / 2, Height * (1 - tuning.launchInset));
            projectileView = projectiles.AsReadOnly();
            trapView = traps.AsReadOnly();
            projectiles.Add(primary);
            Charges = new Charge[tuning.boardColumns * tuning.boardRows];
            float sideInset=MasterConstants.GetFloat("ShotSimulation.chargeSideInset",52);
            for (int row = 0; row < tuning.boardRows; row++)
                for (int col = 0; col < tuning.boardColumns; col++)
                    Charges[row * tuning.boardColumns + col] = new Charge {
                        Position = new Vector2(Mathf.Lerp(sideInset, Width - sideInset, col / (float)(tuning.boardColumns - 1)),
                            Mathf.Lerp(Height * tuning.boardBottom, Height * tuning.boardTop, row / (float)(tuning.boardRows - 1))),
                        Active = false
                    };
            ResetBoard();
            Prepare(false, Width / 2);
        }
        Element RandomElement()
        {
            var weights = tuning.elementWeights;
            float value = (float)random.NextDouble() * (weights.x + weights.y + weights.z + weights.w);
            for (int i = 0; i < 3; i++)
            {
                if (value < weights[i]) return (Element)i;
                value -= weights[i];
            }
            return Element.Neutral;
        }
        SpecialCharge RandomSpecial()
        {
            float roll = (float)random.NextDouble();
            for (int i = 0; i < tuning.specialChargeProbabilities.Length; i++)
            {
                roll -= Mathf.Max(0, tuning.specialChargeProbabilities[i]);
                if (roll < 0) return (SpecialCharge)(i + 1);
            }
            return SpecialCharge.None;
        }
        public void Prepare(bool enemySide, float x)
        {
            if (Moving) throw new InvalidOperationException("Cannot reposition a moving shot");
            EnemySide = enemySide;
            float sidePadding=MasterConstants.GetFloat("ShotSimulation.launchSidePadding",18);
            primary.Position = new Vector2(Mathf.Clamp(x, tuning.shotRadius + sidePadding, Width - tuning.shotRadius - sidePadding),
                Height * (enemySide ? 1 - tuning.launchInset : tuning.launchInset));
        }
        public void SetLaunchX(float x) { if (!Moving) Prepare(false, x); }
        public bool Launch(Vector2 direction, SpiritDefinition spirit, float speedMultiplier = 1)
        {
            float forward = EnemySide ? -direction.y : direction.y;
            if (Moving || spirit == null || !spirit.IsValid || direction.sqrMagnitude <= 0 || forward <= 0) return false;
            if (!EnemySide && (direction.magnitude < tuning.minimumSwipe || forward < tuning.minimumForwardSwipe)) return false;
            // Remember actual launches; a turn's temporary preparation must not overwrite the opponent's last shot position.
            if (EnemySide) enemyLaunchPosition = primary.Position; else playerLaunchPosition = primary.Position;
            actingSpirit = spirit;
            shotElement = spirit.element;
            Matched = Collected = 0;
            WeightedContacts = elapsed = 0;
            DamageWeight=0;
            allClearAt = -1;
            actionSplitUsed = false;
            projectiles.Clear();
            primary.AttackMultiplier = 1;
            primary.Velocity = direction.normalized * Mathf.Min(tuning.maximumShotSpeed, spirit.Speed * Mathf.Max(.01f, speedMultiplier));
            primary.Active = true;
            projectiles.Add(primary);
            Moving = true;
            return true;
        }
        public void ResetBoard()
        {
            allClearAt = -1;
            foreach (var charge in Charges)
            {
                if (charge.Active) continue;
                charge.Element = RandomElement();
                charge.Special = RandomSpecial();
                charge.Active = true;
            }
            ResetCount++;
        }
        public void Tick(float dt)
        {
            if (!Moving || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            float remaining = Mathf.Min(dt, Mathf.Max(0, tuning.maximumShotSeconds - elapsed));
            while (Moving && remaining > .000001f)
            {
                float fastest = 1;
                foreach (var projectile in projectiles)
                    if (projectile.Active) fastest = Mathf.Max(fastest, projectile.Velocity.magnitude);
                float step = Mathf.Min(remaining, Mathf.Max(.001f, tuning.shotRadius) * .4f / fastest);
                elapsed += step;
                remaining -= step;
                int count = projectiles.Count;
                for (int i = 0; i < count && Moving; i++)
                    if (projectiles[i].Active) Advance(projectiles[i], step);
                if (!Moving) return;
                bool anyActive = false;
                foreach (var projectile in projectiles) anyActive |= projectile.Active;
                if (!anyActive || elapsed >= tuning.maximumShotSeconds - .000001f) FinishShot();
                else if (AwaitingAllClearRefill && elapsed - allClearAt >= tuning.allClearRefillDelay)
                    ResetBoard(); // Keep this shot's velocity, clones, contacts, skill level and damage.
            }
            if (Moving && elapsed >= tuning.maximumShotSeconds - .000001f) FinishShot();
        }
        void Advance(Projectile projectile, float dt)
        {
            projectile.Position += projectile.Velocity * dt;
            ReflectWalls(projectile);
            float distance = tuning.shotRadius + tuning.chargeRadius;
            for (int i = 0; i < Charges.Length && Moving; i++)
            {
                var charge = Charges[i];
                if (!charge.Active) continue;
                Vector2 delta = projectile.Position - charge.Position;
                Vector2 normal;
                if (tuning.squareHitboxes)
                {
                    if (Mathf.Abs(delta.x) > distance || Mathf.Abs(delta.y) > distance) continue;
                    bool horizontal = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ||
                        (Mathf.Approximately(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) && Mathf.Abs(projectile.Velocity.x) > Mathf.Abs(projectile.Velocity.y));
                    normal = horizontal
                        ? new Vector2(delta.x == 0 ? -Mathf.Sign(projectile.Velocity.x) : Mathf.Sign(delta.x), 0)
                        : new Vector2(0, delta.y == 0 ? -Mathf.Sign(projectile.Velocity.y) : Mathf.Sign(delta.y));
                }
                else
                {
                    if (delta.sqrMagnitude > distance * distance) continue;
                    normal = delta.sqrMagnitude < .0001f ? -projectile.Velocity.normalized : delta.normalized;
                }
                if (Vector2.Dot(projectile.Velocity, normal) < 0)
                    projectile.Velocity = Vector2.Reflect(projectile.Velocity, normal);
                Collect(i, projectile);
            }
            CheckTraps(projectile);
            projectile.Velocity = Vector2.MoveTowards(projectile.Velocity, Vector2.zero, tuning.deceleration * dt);
            if (projectile.Velocity.magnitude < tuning.stopSpeed)
            {
                projectile.Velocity = Vector2.zero;
                projectile.Active = false;
            }
        }
        void ReflectWalls(Projectile projectile)
        {
            float r = tuning.shotRadius;
            Vector2 position = projectile.Position;
            Vector2 velocity = projectile.Velocity;
            if (position.x < r) { position.x = r; velocity.x = Mathf.Abs(velocity.x); }
            if (position.x > Width - r) { position.x = Width - r; velocity.x = -Mathf.Abs(velocity.x); }
            if (position.y < r) { position.y = r; velocity.y = Mathf.Abs(velocity.y); }
            if (position.y > Height - r) { position.y = Height - r; velocity.y = -Mathf.Abs(velocity.y); }
            projectile.Position = position;
            projectile.Velocity = velocity;
        }
        void Collect(int index, Projectile projectile)
        {
            var charge = Charges[index];
            if (!charge.Active || !Moving) return;
            charge.Active = false;
            Collected++;
            WeightedContacts += projectile.AttackMultiplier;
            DamageWeight=Math.Min(double.MaxValue,DamageWeight+projectile.AttackMultiplier*BattleRules.HitDamageMultiplier(Collected));
            bool same = charge.Element == shotElement;
            if (same) Matched++;
            CollectedCharge?.Invoke(index, same);
            switch (charge.Special)
            {
                case SpecialCharge.AttackBoost:
                    projectile.AttackMultiplier = Mathf.Min(tuning.maximumContactMultiplier, projectile.AttackMultiplier * tuning.specialAttackMultiplier);
                    break;
                case SpecialCharge.SpeedBoost:
                    projectile.Velocity = Vector2.ClampMagnitude(projectile.Velocity * tuning.specialSpeedMultiplier, tuning.maximumShotSpeed);
                    break;
                case SpecialCharge.Split:
                    Split(projectile, tuning.specialSplitTotal);
                    break;
                case SpecialCharge.VerticalMissile:
                case SpecialCharge.HorizontalMissile:
                    for (int i = 0; i < Charges.Length; i++)
                    {
                        bool sameLine = charge.Special == SpecialCharge.VerticalMissile
                            ? i % tuning.boardColumns == index % tuning.boardColumns
                            : i / tuning.boardColumns == index / tuning.boardColumns;
                        if (sameLine) Collect(i, projectile);
                    }
                    break;
                case SpecialCharge.Warp:
                    projectile.Position = EnemySide ? playerLaunchPosition : enemyLaunchPosition;
                    break;
            }
            if (charge.Special != SpecialCharge.None) SpecialTriggered?.Invoke(index, charge.Special);
            if (!actionSplitUsed && actingSpirit.actionSkill == ActionSkill.Split &&
                Collected >= Mathf.Max(1, actingSpirit.splitContactThreshold))
            {
                actionSplitUsed = true;
                Split(projectile, actingSpirit.splitTotalProjectiles);
            }
            // Missile recursion and split projectiles may clear the final cells in one step.
            // The pending timestamp makes this a single event for each emptied board.
            if (!Moving || AwaitingAllClearRefill) return;
            foreach (var remaining in Charges) if (remaining.Active) return;
            allClearAt = elapsed;
            AllClearCount++;
            AllCleared?.Invoke();
        }
        void Split(Projectile source, int total)
        {
            int count = Mathf.Clamp(total, 1, tuning.maximumProjectiles);
            for (int i = 1; i < count && projectiles.Count < tuning.maximumProjectiles; i++)
            {
                float angle = 2 * Mathf.PI * i / count;
                Vector2 velocity = source.Velocity;
                projectiles.Add(new Projectile {
                    Position = source.Position,
                    Velocity = new Vector2(velocity.x * Mathf.Cos(angle) - velocity.y * Mathf.Sin(angle),
                        velocity.x * Mathf.Sin(angle) + velocity.y * Mathf.Cos(angle)),
                    Active = true,
                    IsClone = true,
                    AttackMultiplier = source.AttackMultiplier
                });
            }
        }
        void CheckTraps(Projectile projectile)
        {
            float radius = tuning.shotRadius + tuning.trapRadius;
            for (int i = traps.Count - 1; i >= 0 && Moving; i--)
            {
                var trap = traps[i];
                if (trap.EnemyOwner == EnemySide || trap.TargetElement != shotElement) continue;
                Vector2 delta = projectile.Position - trap.Position;
                if (Mathf.Abs(delta.x) > radius || Mathf.Abs(delta.y) > radius) continue;
                traps.RemoveAt(i);
                TrapTriggered?.Invoke(trap.EnemyOwner, trap.Damage);
            }
        }
        void FinishShot()
        {
            CancelShot();
            for (int i = traps.Count - 1; i >= 0; i--)
                if (traps[i].EnemyOwner != EnemySide && --traps[i].OpponentTurns <= 0) traps.RemoveAt(i);
            if (actingSpirit.actionSkill != ActionSkill.Trap) return;
            if (traps.Count >= tuning.maximumTraps) traps.RemoveAt(0);
            traps.Add(new Trap {
                Position = primary.Position,
                EnemyOwner = EnemySide,
                TargetElement = actingSpirit.trapTargetElement,
                Damage = Mathf.Max(0, actingSpirit.trapDamage),
                OpponentTurns = Mathf.Max(1, actingSpirit.trapOpponentTurns)
            });
        }
        public void CancelShot()
        {
            Moving = false;
            allClearAt = -1;
            foreach (var projectile in projectiles)
            {
                projectile.Active = false;
                projectile.Velocity = Vector2.zero;
            }
        }
    }
}
