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

public partial class UnitState : Schema {
#if UNITY_5_3_OR_NEWER
[Preserve]
#endif
public UnitState() { }
	[Type(0, "string")]
	public string id = default(string);

	[Type(1, "string")]
	public string ownerId = default(string);

	[Type(2, "string")]
	public string pieceId = default(string);

	[Type(3, "number")]
	public float x = default(float);

	[Type(4, "number")]
	public float y = default(float);

	[Type(5, "number")]
	public float hp = default(float);

	[Type(6, "number")]
	public float mana = default(float);
}

