using UnityEngine;

public class PlayerState
{
    protected PlayerAnimatorController animatorController;
    protected PlayerStateMachine stateMachine;
    protected PlayerController core;

    protected float timer;
    protected float startTime;
    protected string animBoolName;

    public PlayerState(string _animBoolName, PlayerAnimatorController _animatorController, PlayerStateMachine _stateMachine)
    {
        this.animBoolName = _animBoolName;
        this.animatorController = _animatorController;
        this.stateMachine = _stateMachine;
        this.core = PlayerController.instance;
    }

    public virtual void Enter()
    {
        startTime = Time.time;
        animatorController.anim.SetBool(animBoolName, true);
    }

    public virtual void Exit()
    {
        animatorController.anim.SetBool(animBoolName, false);
    }

    public virtual void Update()
    {

    }

    

    public virtual void AnimationFinishTrigger()
    {

    }
}