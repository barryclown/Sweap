using Unity.VisualScripting;
using UnityEngine;

public class PlayerIdleState : PlayerState
{
    public PlayerIdleState(string animBoolName, PlayerAnimatorController player, PlayerStateMachine stateMachine)
        : base(animBoolName, player, stateMachine)
    {
    }

    public override void Enter()
    {
        base.Enter();
        core.isBlocking = false;
    }

     public override void Update()
    {
        base.Update();
        
        Vector2 vel = PlayerController.instance.rb.linearVelocity;
        if (vel.magnitude > 0.1f)
        {
            stateMachine.ChangeState(PlayerAnimatorController.instance.moveState);
        }
    }

}