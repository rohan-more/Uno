using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DebugHandBootstrap : MonoBehaviour
{
    [SerializeField] private HandView handView;
    [SerializeField] private CardDatabase database;

    void Start()
    {
        database.Initialize();

        var hand = TestDeckBuilder.BuildRandomHand(database, 15);
        handView.BuildHand(hand);
    }
}
