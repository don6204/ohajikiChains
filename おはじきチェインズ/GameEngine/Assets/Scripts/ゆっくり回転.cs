using Ohajiki.Engine;

// 公開フィールドはインスペクターから調整・保存できます。
public sealed class ゆっくり回転 : OhajikiBehaviour
{
    [Expose("回転速度", "1秒あたりの回転角度")]
    public float Speed = 30f;

    public override void OnStart()
    {
        Log(GameObject.Name + " を開始しました");
    }

    public override void OnUpdate()
    {
        Transform.Rotation += Speed * DeltaTime;
	　
    }

    public override void OnCollision(GameObject other) { }
    public override void OnStop() { }
}