using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;
using HanziDefend.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace HanziDefend.Tests.PlayMode
{
    /// <summary>
    /// Drags driven through <see cref="ExecuteEvents"/>, the way Unity's input module drives them.
    ///
    /// <para><b>Why this file exists.</b> Every other deploy drag test calls
    /// <c>screen.BeginCardDrag/DragTo/EndDrag</c> directly. That verifies the logic but bypasses the
    /// EventSystem entirely — so a bug in the <i>dispatch</i> layer passes with everything green.
    /// One did: the drag handle lived on the hand card's root GameObject, and BeginCardDrag
    /// deactivated that same object to hide the card. Unity routes OnDrag/OnEndDrag to
    /// <c>PointerEventData.pointerDrag</c>, and ExecuteEvents skips components on an inactive
    /// object without raising anything, so every event after OnBeginDrag was silently dropped: the
    /// card froze after a pixel of movement and the drag never ended.</para>
    ///
    /// <para>These tests dispatch through the handle object exactly as the input module does, so the
    /// assertion "the handle can still receive events" is a real one.</para>
    /// </summary>
    public sealed class DeployDragEventChainTests
    {
        private readonly List<GameObject> cleanup = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = cleanup.Count - 1; index >= 0; index--)
            {
                if (cleanup[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(cleanup[index]);
                }
            }
            cleanup.Clear();

            EventSystem[] eventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < eventSystems.Length; index++)
            {
                if (eventSystems[index] != null && eventSystems[index].gameObject.name == "M1 EventSystem")
                {
                    UnityEngine.Object.DestroyImmediate(eventSystems[index].gameObject);
                }
            }
        }

        /// <summary>
        /// The regression guard. Begin a drag through the event system, then keep dispatching drag
        /// events to the same handle the way the input module would. Every dispatch must find a live
        /// handler; a single false here means the rest of the gesture is being thrown away.
        /// </summary>
        [Test]
        public void EventDrivenDrag_KeepsDeliveringDragAndEndDragToTheSameHandle()
        {
            DeployScreen screen = CreateScreen(out CardOfferItem card, "dao");
            GameObject handle = HandleObjectFor(screen, card);
            UnitFootprint footprint = screen.HandCardFootprint(card);

            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = screen.DragScreenPointForAnchor(new GridCoordinate(2, 2), footprint)
            };

            bool began = ExecuteEvents.Execute(handle, pointer, ExecuteEvents.beginDragHandler);
            Assert.That(began, Is.True, "OnBeginDrag must reach the handle.");
            Assert.That(screen.IsDragging, Is.True, "the screen should now own a drag session");

            // This is what the input module does: it remembers the object that took the drag and
            // sends every later event straight to it.
            pointer.pointerDrag = handle;

            var route = new[]
            {
                new GridCoordinate(3, 2), new GridCoordinate(3, 3),
                new GridCoordinate(2, 3), new GridCoordinate(4, 3)
            };
            for (int step = 0; step < route.Length; step++)
            {
                Assert.That(handle.activeInHierarchy, Is.True,
                    $"step {step}: the drag handle's object must stay active for the whole gesture.");

                pointer.position = screen.DragScreenPointForAnchor(route[step], footprint);
                bool dragged = ExecuteEvents.Execute(
                    pointer.pointerDrag, pointer, ExecuteEvents.dragHandler);

                Assert.That(dragged, Is.True,
                    $"step {step}: OnDrag was dropped — ExecuteEvents found no live handler.");
                Assert.That(screen.IsDragging, Is.True, $"step {step}: the drag must still be running");
                Assert.That(screen.DragAnchor, Is.EqualTo(route[step]),
                    $"step {step}: the drag must have actually followed the pointer.");
            }

            bool ended = ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);

            Assert.That(ended, Is.True, "OnEndDrag was dropped — the drag session would never close.");
            Assert.That(screen.IsDragging, Is.False, "the drag session must be closed");
            Assert.That(screen.LastDragOutcome, Is.Not.EqualTo(DeployDragOutcome.None));
        }

        /// <summary>
        /// The hidden source card must be invisible but still alive: alpha 0 and click-through, with
        /// its GameObject active so it can keep receiving the gesture.
        /// </summary>
        [Test]
        public void SourceCard_IsHiddenByCanvasGroupNotByDeactivation()
        {
            DeployScreen screen = CreateScreen(out CardOfferItem card, "dao");
            GameObject handle = HandleObjectFor(screen, card);
            UnitFootprint footprint = screen.HandCardFootprint(card);

            var pointer = new PointerEventData(EventSystem.current)
            {
                position = screen.DragScreenPointForAnchor(new GridCoordinate(2, 2), footprint)
            };
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.beginDragHandler);

            Assert.That(handle.activeSelf, Is.True, "the handle's object must never be deactivated");
            CanvasGroup group = handle.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null, "hiding is done with a CanvasGroup");
            Assert.That(group.alpha, Is.EqualTo(0f), "the source card reads as picked up");
            Assert.That(group.blocksRaycasts, Is.False, "and must not swallow pointer events");

            pointer.pointerDrag = handle;
            ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);

            Assert.That(handle.activeSelf, Is.True);
        }

        /// <summary>An illegal release still has to travel the event chain and bounce the card back.</summary>
        [UnityTest]
        public IEnumerator EventDrivenDrag_OnAnIllegalCellStillEndsAndReturnsTheCard()
        {
            DeployScreen screen = CreateScreen(out CardOfferItem card, "dao");
            GameObject handle = HandleObjectFor(screen, card);
            UnitFootprint footprint = screen.HandCardFootprint(card);
            int placementsBefore = screen.Economy.Grid.Placements.Count;

            var pointer = new PointerEventData(EventSystem.current)
            {
                position = screen.DragScreenPointForAnchor(new GridCoordinate(2, 2), footprint)
            };
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.beginDragHandler);
            pointer.pointerDrag = handle;

            pointer.position = screen.DragScreenPointForAnchor(new GridCoordinate(0, 0), footprint);
            Assert.That(ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.dragHandler), Is.True);
            Assert.That(ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler), Is.True);

            Assert.That(screen.LastDragOutcome, Is.EqualTo(DeployDragOutcome.Rejected));
            Assert.That(screen.Economy.Grid.Placements, Has.Count.EqualTo(placementsBefore));

            float deadline = Time.realtimeSinceStartup + 2f;
            while (screen.IsReturningToHand && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(screen.IsReturningToHand, Is.False);
            Assert.That(screen.IsDragging, Is.False);
            CanvasGroup group = handle.GetComponent<CanvasGroup>();
            Assert.That(group.alpha, Is.EqualTo(1f), "the returned card is visible again");
            Assert.That(group.blocksRaycasts, Is.True, "and grabbable again");
        }

        /// <summary>A legal release through the event chain commits through CardEconomy.</summary>
        [Test]
        public void EventDrivenDrag_OnALegalCellPlacesTheUnit()
        {
            DeployScreen screen = CreateScreen(out CardOfferItem card, "dao");
            GameObject handle = HandleObjectFor(screen, card);
            UnitFootprint footprint = screen.HandCardFootprint(card);
            int placementsBefore = screen.Economy.Grid.Placements.Count;

            var pointer = new PointerEventData(EventSystem.current)
            {
                position = screen.DragScreenPointForAnchor(new GridCoordinate(2, 2), footprint)
            };
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.beginDragHandler);
            pointer.pointerDrag = handle;
            pointer.position = screen.DragScreenPointForAnchor(new GridCoordinate(3, 3), footprint);
            ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);

            Assert.That(screen.LastDragOutcome, Is.EqualTo(DeployDragOutcome.Placed));
            Assert.That(screen.Economy.Grid.Placements, Has.Count.EqualTo(placementsBefore + 1));
        }

        /// <summary>
        /// A hand rebuild mid-drag (buying an unlock card, an ad reward, a refresh) must not destroy
        /// the object holding the gesture — that would break the chain exactly like deactivating it.
        /// </summary>
        [Test]
        public void HandRebuildDuringDrag_LeavesTheDragHandleAliveAndReceiving()
        {
            DeployScreen screen = CreateScreen(out CardOfferItem card, "dao");
            GameObject handle = HandleObjectFor(screen, card);
            UnitFootprint footprint = screen.HandCardFootprint(card);

            var pointer = new PointerEventData(EventSystem.current)
            {
                position = screen.DragScreenPointForAnchor(new GridCoordinate(2, 2), footprint)
            };
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.beginDragHandler);
            pointer.pointerDrag = handle;

            // Rebuilding the hand under the player's fingers.
            screen.RefreshAll();

            Assert.That(handle, Is.Not.Null);
            Assert.That(handle.activeInHierarchy, Is.True,
                "the dragged card must survive a hand rebuild, parked rather than destroyed");

            pointer.position = screen.DragScreenPointForAnchor(new GridCoordinate(3, 3), footprint);
            Assert.That(ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.dragHandler), Is.True,
                "OnDrag must still land after the hand rebuilt");
            Assert.That(ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler), Is.True,
                "OnEndDrag must still land after the hand rebuilt");
            Assert.That(screen.IsDragging, Is.False);
        }

        /// <summary>The ghost shows the card's artwork, not just a coloured block with a word on it.</summary>
        [Test]
        public void DragGhost_CarriesTheCardArtworkNotOnlyText()
        {
            var sprite = Sprite.Create(
                new Texture2D(8, 8), new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f));
            DeployScreen screen = CreateScreen(out CardOfferItem card, "dao", new SingleSpriteArtSource(sprite));
            UnitFootprint footprint = screen.HandCardFootprint(card);

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(new GridCoordinate(2, 2), footprint));

            Transform ghost = screen.transform.Find("Drag Layer/Drag Ghost");
            Assert.That(ghost, Is.Not.Null, "the ghost must exist while dragging");
            Image[] images = ghost.GetComponentsInChildren<Image>(true);
            Image art = images.FirstOrDefault(image => image.sprite == sprite);

            Assert.That(art, Is.Not.Null,
                "the ghost must carry the source card's sprite — otherwise the picture vanishes on pickup.");
            Assert.That(art.color.a, Is.GreaterThan(0.5f), "the artwork must be clearly visible");
            Assert.That(ghost.GetComponentsInChildren<Text>(true), Is.Not.Empty, "and keep its label");

            screen.EndDrag();
            UnityEngine.Object.DestroyImmediate(sprite);
        }

        // ------------------------------------------------------------------------------- helpers

        /// <summary>
        /// The GameObject Unity would route the gesture to: the hand card's root, which is where the
        /// drag handle lives. Found by name because the handle type is internal to the View assembly.
        /// </summary>
        private static GameObject HandleObjectFor(DeployScreen screen, CardOfferItem card)
        {
            Transform hand = screen.transform.Find("Hand Panel/Cards");
            Assert.That(hand, Is.Not.Null, "the hand row must exist");

            foreach (Transform child in hand)
            {
                if (child.name.Contains(card.ContentId, StringComparison.Ordinal))
                {
                    // Sanity: this object must genuinely accept begin-drag, or the test proves nothing.
                    Assert.That(
                        ExecuteEvents.GetEventHandler<IBeginDragHandler>(child.gameObject),
                        Is.EqualTo(child.gameObject),
                        $"'{child.name}' is expected to be the drag handler itself.");
                    return child.gameObject;
                }
            }

            throw new AssertionException($"No hand card found for '{card.ContentId}'.");
        }

        private DeployScreen CreateScreen(
            out CardOfferItem card,
            string unitId,
            IBattleArtSource artSource = null)
        {
            GameConfig config = GameConfig.Load();
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC4D001u);
            economy.State.Coins = int.MaxValue;

            var canvasObject = new GameObject(
                "Drag Chain Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cleanup.Add(canvasObject);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1080f, 1920f);

            var screenObject = new GameObject("DeployRoot", typeof(RectTransform));
            screenObject.transform.SetParent(canvasObject.transform, false);
            cleanup.Add(screenObject);
            DeployScreen screen = screenObject.AddComponent<DeployScreen>();
            screen.Initialize(config, economy, artSource ?? new NullArtSource(), null);

            for (int attempt = 0; attempt < 4000; attempt++)
            {
                CardOfferItem match = economy.CurrentOffer.Cards.FirstOrDefault(
                    value => value.Category == CardCategory.Unit
                             && string.Equals(value.ContentId, unitId, StringComparison.Ordinal));
                if (match != null)
                {
                    card = match;
                    return screen;
                }

                economy.State.Coins = int.MaxValue;
                Assert.That(economy.TryRefresh(out _), Is.True);
            }

            throw new AssertionException($"'{unitId}' never appeared within the deterministic bound.");
        }

        private sealed class NullArtSource : IBattleArtSource
        {
            public Sprite Find(string assetKey) => null;
        }

        private sealed class SingleSpriteArtSource : IBattleArtSource
        {
            private readonly Sprite sprite;

            internal SingleSpriteArtSource(Sprite value)
            {
                sprite = value;
            }

            public Sprite Find(string assetKey) => sprite;
        }
    }
}
