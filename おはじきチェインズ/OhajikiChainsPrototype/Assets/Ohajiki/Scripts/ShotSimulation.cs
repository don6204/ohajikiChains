using System;
using UnityEngine;
namespace Ohajiki
{
    public sealed class ShotSimulation
    {
        public sealed class Charge { public Vector2 Position; public Element Element; public bool Active = true; }
        public const float Width = 624, Height = 650;
        public readonly Charge[] Charges;
        public Vector2 Position { get; private set; }
        public Vector2 Velocity { get; private set; }
        public int Matched { get; private set; }
        public int Collected { get; private set; }
        public int ResetCount { get; private set; }
        public bool Moving { get; private set; }
        public bool EnemySide { get; private set; }
        public event Action<int, bool> CollectedCharge;
        readonly BattleTuning tuning;
        readonly System.Random random;
        Element shotElement;
        float elapsed;
        public ShotSimulation(BattleTuning tuning)
        {
            this.tuning = tuning; random = new System.Random(tuning.seed);
            Charges = new Charge[tuning.boardColumns * tuning.boardRows];
            for (int row = 0; row < tuning.boardRows; row++)
                for (int col = 0; col < tuning.boardColumns; col++)
                    Charges[row * tuning.boardColumns + col] = new Charge {
                        Position = new Vector2(Mathf.Lerp(52, Width-52, col/(float)(tuning.boardColumns-1)),
                        Mathf.Lerp(Height*tuning.boardBottom, Height*tuning.boardTop, row/(float)(tuning.boardRows-1))) };
            ResetBoard(); Prepare(false, Width/2);
        }
        Element RandomElement()
        {
            var weights = tuning.elementWeights;
            float value = (float)random.NextDouble() * (weights.x+weights.y+weights.z+weights.w);
            for (int i = 0; i < 3; i++) { if (value < weights[i]) return (Element)i; value -= weights[i]; }
            return Element.Neutral;
        }
        public void Prepare(bool enemySide, float x)
        {
            if (Moving) throw new InvalidOperationException("Cannot reposition a moving shot");
            EnemySide = enemySide;
            Position = new Vector2(Mathf.Clamp(x, tuning.shotRadius+18, Width-tuning.shotRadius-18),
                Height * (enemySide ? 1-tuning.launchInset : tuning.launchInset));
        }
        public void SetLaunchX(float x) { if (!Moving) Prepare(false,x); }
        public bool Launch(Vector2 direction, SpiritDefinition spirit)
        {
            float forward = EnemySide ? -direction.y : direction.y;
            if (Moving || spirit == null || !spirit.IsValid || direction.sqrMagnitude <= 0 || forward <= 0) return false;
            // Gesture sensitivity belongs to human input. AI chooses a direction directly.
            if (!EnemySide && (direction.magnitude < tuning.minimumSwipe || forward < tuning.minimumForwardSwipe)) return false;
            shotElement = spirit.element; Matched = Collected = 0; elapsed = 0;
            Velocity = direction.normalized * spirit.Speed; Moving = true; return true;
        }
        public void ResetBoard()
        {
            foreach (var charge in Charges) { charge.Active = true; charge.Element = RandomElement(); }
            ResetCount++;
        }
        public void Tick(float dt)
        {
            if (!Moving || dt <= 0) return;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt * Velocity.magnitude / (tuning.shotRadius * .4f)));
            float step = dt / steps;
            for (int i = 0; i < steps && Moving; i++) Advance(step);
        }
        void Advance(float dt)
        {
            elapsed += dt; Position += Velocity * dt; float r = tuning.shotRadius;
            if (Position.x < r) { Position = new Vector2(r,Position.y); Velocity = new Vector2(Mathf.Abs(Velocity.x),Velocity.y); }
            if (Position.x > Width-r) { Position = new Vector2(Width-r,Position.y); Velocity = new Vector2(-Mathf.Abs(Velocity.x),Velocity.y); }
            if (Position.y < r) { Position = new Vector2(Position.x,r); Velocity = new Vector2(Velocity.x,Mathf.Abs(Velocity.y)); }
            if (Position.y > Height-r) { Position = new Vector2(Position.x,Height-r); Velocity = new Vector2(Velocity.x,-Mathf.Abs(Velocity.y)); }
            float distance = r + tuning.chargeRadius;
            for (int i = 0; i < Charges.Length; i++)
            {
                var charge = Charges[i]; if (!charge.Active) continue;
                Vector2 delta = Position-charge.Position;
                Vector2 normal;
                if(tuning.squareHitboxes)
                {
                    if(Mathf.Abs(delta.x)>distance || Mathf.Abs(delta.y)>distance)continue;
                    // The least penetration axis is the impacted face. Ties use incoming motion.
                    bool horizontal=Mathf.Abs(delta.x)>Mathf.Abs(delta.y) ||
                        (Mathf.Approximately(Mathf.Abs(delta.x),Mathf.Abs(delta.y)) && Mathf.Abs(Velocity.x)>Mathf.Abs(Velocity.y));
                    normal=horizontal?new Vector2(delta.x==0?-Mathf.Sign(Velocity.x):Mathf.Sign(delta.x),0):
                        new Vector2(0,delta.y==0?-Mathf.Sign(Velocity.y):Mathf.Sign(delta.y));
                }
                else
                {
                    if(delta.sqrMagnitude>distance*distance)continue;
                    normal=delta.sqrMagnitude<.0001f?-Velocity.normalized:delta.normalized;
                }
                charge.Active = false; Collected++;
                bool same = charge.Element == shotElement; if (same) Matched++;
                if (Vector2.Dot(Velocity,normal) < 0) Velocity = Vector2.Reflect(Velocity,normal);
                CollectedCharge?.Invoke(i,same);
            }
            Velocity = Vector2.MoveTowards(Velocity,Vector2.zero,tuning.deceleration*dt);
            if (Velocity.magnitude < tuning.stopSpeed || elapsed >= tuning.maximumShotSeconds) { Velocity = Vector2.zero; Moving = false; }
        }
    }
}
