using System;
using UnityEngine;

namespace IronbarkCoop
{
    [Serializable]
    public sealed class WorldState
    {
        public bool doorOpen, locked, fridgeOpen, fridgeLight;
        public Quaternion doorRotation, fridgeRotation;
        public Vector3 doorPosition, fridgePosition;
    }

    // Lab readout of a few tower objects; replication itself is done by StorySync for every story object.
    internal static class WorldProbe
    {
        public static WorldState Snapshot()
        {
            var manager = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            if (!manager) return null;
            return new WorldState {
                doorOpen = manager.isDoorOpen, locked = manager.isLocked,
                fridgeOpen = manager.fridgeDoorOpen, fridgeLight = manager.fridgeLight.activeSelf,
                doorRotation = manager.door.transform.localRotation,
                fridgeRotation = manager.fridgeDoor.transform.localRotation,
                doorPosition = manager.door.transform.position,
                fridgePosition = manager.fridgeDoor.transform.position
            };
        }
    }
}
