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

public partial class RoomState : Schema {
#if UNITY_5_3_OR_NEWER
[Preserve]
#endif
public RoomState() { }
	[Type(0, "number")]
	public float startTick = default(float);

	[Type(1, "ref", typeof(BoardState))]
	public BoardState board = null;

	[Type(2, "map", typeof(MapSchema<MovingState>))]
	public MapSchema<MovingState> moving = null;
}

