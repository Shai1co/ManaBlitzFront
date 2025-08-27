// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 3.0.59
// 

using Colyseus.Schema;
#if UNITY_5_3_OR_NEWER
using UnityEngine.Scripting;
#endif

public partial class RoomState : Schema {
#if UNITY_5_3_OR_NEWER
[Preserve]
#endif
public RoomState() { }
	[Type(0, "number")]
	public float startTick = default(float);

	[Type(1, "number")]
	public float serverTick = default(float);

	[Type(2, "ref", typeof(BoardState))]
	public BoardState board = null;

	[Type(3, "map", typeof(MapSchema<MovingState>))]
	public MapSchema<MovingState> moving = null;
}

