// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 3.0.56
// 

using Colyseus.Schema;
#if UNITY_5_3_OR_NEWER
using UnityEngine.Scripting;
#endif

public partial class MovingState : Schema {
#if UNITY_5_3_OR_NEWER
[Preserve]
#endif
public MovingState() { }
	[Type(0, "string")]
	public string unitId = default(string);

	[Type(1, "number")]
	public float startTick = default(float);

	[Type(2, "number")]
	public float endTick = default(float);

	[Type(3, "number")]
	public float fromX = default(float);

	[Type(4, "number")]
	public float fromY = default(float);

	[Type(5, "number")]
	public float toX = default(float);

	[Type(6, "number")]
	public float toY = default(float);

	[Type(7, "string")]
	public string reason = default(string);
}

