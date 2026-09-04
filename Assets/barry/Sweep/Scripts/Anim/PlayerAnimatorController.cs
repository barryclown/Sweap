using UnityEngine;

public class PlayerAnimatorController : MonoBehaviour
{
    public static PlayerAnimatorController instance;

    public Animator anim { get; private set; }
    public PlayerStateMachine stateMachine { get; private set; }

    public PlayerIdleState idleState { get; private set; }
    public PlayerMoveState moveState { get; private set; }

    
    public PowerState powerState { get; private set; }     // 蓄力
    public ReleaseState releaseState { get; private set; } // 釋放

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        anim = GetComponent<Animator>();
        stateMachine = new PlayerStateMachine();
    }

    private void Start()
    {
        idleState = new PlayerIdleState("Idle", this, stateMachine);
        moveState = new PlayerMoveState("Move", this, stateMachine);

       
        powerState = new PowerState("Power", this, stateMachine);
        releaseState = new ReleaseState("Release", this, stateMachine);

        stateMachine.Initialize(idleState);
    }

    private void Update()
    {
      
        stateMachine.currentState.Update();
    }


   
}