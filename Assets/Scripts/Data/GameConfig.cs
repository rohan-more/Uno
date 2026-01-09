using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(menuName = "UNO/Game Config")]
public class GameConfig : ScriptableObject
{
    public List<RuleDefinitionSO> rules;
}