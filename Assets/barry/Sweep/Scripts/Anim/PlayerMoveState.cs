using UnityEngine;

public class PlayerMoveState : PlayerState
{
    public PlayerMoveState(string animBoolName, PlayerAnimatorController player, PlayerStateMachine stateMachine)
        : base(animBoolName, player, stateMachine)
    {
    }

    public override void Update()
    {
        base.Update(); // 保持父類別 Update

        // 1. 獲取速度
        Vector2 vel = PlayerController.instance.rb.linearVelocity;
        float speed = vel.magnitude;

        // 2. ★關鍵修正：必須更新 Speed 參數，否則 BlendTree 不會動
        PlayerAnimatorController.instance.anim.SetFloat("Speed", speed);

        // 3. 更新方向參數 (保持你原本的邏輯)
        if (speed > 0.001f)
        {
            Vector2 dir = vel.normalized;
            PlayerAnimatorController.instance.anim.SetFloat("VelX", dir.x);
            PlayerAnimatorController.instance.anim.SetFloat("VelY", dir.y);
        }

        // 4. 切換回 Idle 的條件
        // 建議稍微提高一點門檻 (例如 0.01 或 0.1)，避免浮點數誤差導致抖動
        if (speed < 0.01f)
        {
            stateMachine.ChangeState(PlayerAnimatorController.instance.idleState);
        }
    }
}