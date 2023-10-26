using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Matryoshka.Abilities
{
    public enum AbilityType : int
    {
        Primary = 1,
        Secondary = 2,
        Special1 = 3,
        Special2 = 4,
        Special3 = 5,
        BulletHell = 6,
        Special4 = 7,
        None = 0

    }

    [CreateAssetMenu(fileName = "New Ability", menuName = "Ability")]
    public class Ability : ScriptableObject
    {
        public float windUpTime;
        public float castTime;
        public float windDownTime;
        public float cooldownTime;

        public List<Effect.Effect> windUp;
        public List<Effect.Effect> cast;
        public List<Effect.Effect> windDown;
    }
}

