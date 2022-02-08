using UnityEngine;
using NUnit.Framework;

namespace TestProject.RuntimeTests
{
    public class SomethingTests
    {
        private GameObject m_GameObject;
        private Something m_Component;

        [SetUp]
        public void Setup()
        {
            m_GameObject = new GameObject(nameof(SomethingTests));
            m_Component = m_GameObject.AddComponent<Something>();
        }

        [TearDown]
        public void Teardown()
        {
            Object.DestroyImmediate(m_GameObject);
            m_GameObject = null;
            m_Component = null;
        }

        [Test]
        public void TestSomething()
        {
            Assert.IsNotNull(m_Component);
            m_Component.PrintRpcTable();
        }
    }
}
