using UnityEngine;

public enum GameMode { Solo, Versus2P, Coop2P, Alt2P }

public abstract class GameManager : MonoBehaviour
{
    public MetaGameManager meta;
    public GameDef Def;
    public GameMode Mode;
    public int ScoreP1;
    public int ScoreP2;
    public bool Running;

    public virtual void Begin() {}
    public virtual void StartMode(GameMode mode) { Mode = mode; Running = true; OnStartMode(); }
    public virtual void OnStartMode() {}
    public virtual void StopMode() { Running = false; }
    public virtual void QuitToMenu() { meta.QuitToSelection(); }
    public virtual void ResetScores() { ScoreP1=0; ScoreP2=0; }

    protected bool BtnA(int p=1) => p==1 ? Input.GetButton("Fire1") || Input.GetKey(KeyCode.Z) : Input.GetKey(KeyCode.Comma);
    protected bool BtnB(int p=1) => p==1 ? Input.GetButton("Fire2") || Input.GetKey(KeyCode.X) : Input.GetKey(KeyCode.Period);
    protected float AxisH(int p=1) => p==1 ? Input.GetAxisRaw("Horizontal") + (Input.GetKey(KeyCode.LeftArrow)?-1:0) + (Input.GetKey(KeyCode.RightArrow)?1:0)
                                           : (Input.GetKey(KeyCode.J)?-1:0)+(Input.GetKey(KeyCode.L)?1:0);
    protected float AxisV(int p=1) => p==1 ? Input.GetAxisRaw("Vertical") + (Input.GetKey(KeyCode.DownArrow)?-1:0) + (Input.GetKey(KeyCode.UpArrow)?1:0)
                                           : (Input.GetKey(KeyCode.K)?-1:0)+(Input.GetKey(KeyCode.I)?1:0);
}
