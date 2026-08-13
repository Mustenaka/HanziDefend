using System.Collections;
using HanziDefend.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HanziDefend.Tests.PlayMode
{
    public sealed class PlayModePipelineTests
    {
        [UnityTest]
        public IEnumerator Bootstrap_CanRunInPlayMode()
        {
            GameObject gameObject = new GameObject("PlayMode Test Bootstrap");
            BattleBootstrap bootstrap = gameObject.AddComponent<BattleBootstrap>();

            Assert.That(bootstrap, Is.Not.Null);
            yield return null;

            Object.Destroy(gameObject);
        }
    }
}
