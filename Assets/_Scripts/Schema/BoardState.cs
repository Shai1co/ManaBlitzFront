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

public partial class BoardState : Schema {
#if UNITY_5_3_OR_NEWER
[Preserve]
#endif
public BoardState() { }
	[Type(0, "number")]
	public float width = default(float);

	[Type(1, "number")]
	public float height = default(float);

	[Type(2, "map", typeof(MapSchema<UnitState>))]
	public MapSchema<UnitState> units = null;
}

