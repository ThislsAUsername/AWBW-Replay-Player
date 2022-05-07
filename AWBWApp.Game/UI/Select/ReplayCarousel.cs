﻿using System;
using System.Collections.Generic;
using System.Linq;
using AWBWApp.Game.API.Replay;
using AWBWApp.Game.IO;
using AWBWApp.Game.UI.Components;
using osu.Framework.Allocation;
using osu.Framework.Caching;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Layout;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace AWBWApp.Game.UI.Select
{
    public class ReplayCarousel : CompositeDrawable
    {
        public bool AllowSelection = true;
        public Action ReplaysChanged;
        public Action<ReplayInfo> SelectionChanged;

        public override bool HandleNonPositionalInput => AllowSelection;
        public override bool HandlePositionalInput => AllowSelection;

        public override bool PropagatePositionalInputSubTree => AllowSelection;
        public override bool PropagateNonPositionalInputSubTree => AllowSelection;

        public bool ReplaysLoaded { get; private set; }

        private CarouselRoot rootCarouselItem;

        public ReplayInfo SelectedReplayData => selectedReplay?.ReplayInfo;

        private CarouselReplay selectedReplay;
        private TextBox searchTextBox;
        private FilterDropdown searchDropdown;
        private Container searchContainer;

        private const float pixels_offscreen_before_unloading_replay = 1024;

        private const float pixels_offsecreen_before_preloading_replay = 512;

        protected readonly CarouselScrollContainer Scroll;
        private readonly List<CarouselItem> visibleItems = new List<CarouselItem>();

        private readonly Cached itemsCache = new Cached();
        private readonly DrawablePool<DrawableCarouselReplay> setPool = new DrawablePool<DrawableCarouselReplay>(100);

        private (int first, int last) displayedRange;

        private float visibleHalfHeight => DrawHeight / 2;
        private float visibleBottomBound => Scroll.Current + DrawHeight;
        private float visibleUpperBound => Scroll.Current;

        private const float panel_padding = 5;

        private float? scrollTarget;
        private bool firstScroll = true;

        public IEnumerable<ReplayInfo> Replays
        {
            get => replays.Select(g => g.ReplayInfo);
            set => loadReplays(value); //Todo: Look into alternative ways to test this
        }

        [Resolved]
        private ReplayManager replayManager { get; set; }

        [Resolved]
        private MapFileStorage mapStorage { get; set; }

        private IEnumerable<CarouselReplay> replays => rootCarouselItem.Children.OfType<CarouselReplay>();

        private PendingScrollOperation pendingScrollOperation = PendingScrollOperation.None;

        private const float header_height = 70;

        public ReplayCarousel()
        {
            rootCarouselItem = new CarouselRoot(this);
            InternalChildren = new Drawable[]
            {
                new AWBWContextMenuContainer()
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        setPool,
                        Scroll = new CarouselScrollContainer
                        {
                            Position = new Vector2(0, header_height)
                        },
                        searchContainer = new Container()
                        {
                            RelativeSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                new BlockingLayer(true, 0.75f)
                                {
                                    BlockKeyEvents = false,
                                    RelativeSizeAxes = Axes.X,
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Height = header_height,
                                    Colour = new Color4(20, 20, 20, 100),
                                },
                                new Box()
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Height = 30,
                                    Colour = new Color4(20, 20, 20, 200)
                                },
                                searchTextBox = new BasicTextBox
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Position = new Vector2(0, 30),
                                    Height = 35f,
                                    Padding = new MarginPadding { Top = 5, Horizontal = 5 },
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    PlaceholderText = "Search Here"
                                },
                                searchDropdown = new FilterDropdown()
                                {
                                    Position = new Vector2(-5, 35),
                                    Size = new Vector2(100, 25),
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                }
                            }
                        }
                    }
                }
            };

            searchTextBox.OnCommit += (x, y) => onSearchTextChange(x.Text, searchDropdown.Current.Value);
            searchDropdown.Current.BindValueChanged(x => onSearchTextChange(searchTextBox.Text, x.NewValue));
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            replayManager.ReplayAdded += replayAdded;
            replayManager.ReplayChanged += replayAdded;
            replayManager.ReplayRemoved += replayRemoved;

            if (!replays.Any())
                loadReplays(replayManager.GetAllKnownReplays());
        }

        private void loadReplays(IEnumerable<ReplayInfo> replays)
        {
            CarouselRoot newRoot = new CarouselRoot(this);

            newRoot.AddChildren(replays.Select(createCarouselReplay));

            rootCarouselItem = newRoot;
            if (selectedReplay != null && !replays.Contains(selectedReplay.ReplayInfo))
                selectedReplay = null;

            Scroll.Clear(false);
            itemsCache.Invalidate();
            ScrollToSelected();

            ScheduleAfterChildren(() =>
            {
                ReplaysChanged?.Invoke();
                ReplaysLoaded = true;
                if (selectedReplay == null)
                    SelectionChanged?.Invoke(null);

                itemsCache.Invalidate();
            });
        }

        private void replayRemoved(ReplayInfo addedReplay) => RemoveReplay(addedReplay);
        private void replayAdded(ReplayInfo addedReplay) => UpdateReplay(addedReplay);

        public void UpdateReplay(ReplayInfo replayInfo)
        {
            CarouselReplay existingReplay = replays.FirstOrDefault(r => r.ReplayInfo.ID == replayInfo.ID);
            var newItem = createCarouselReplay(replayInfo);

            if (existingReplay != null)
                rootCarouselItem.RemoveChild(existingReplay);

            rootCarouselItem.AddChild(newItem);
            newItem.State.Value = CarouselItemState.Selected;

            itemsCache.Invalidate();
            Schedule(() => ReplaysChanged?.Invoke());
        }

        public void RemoveReplay(ReplayInfo replayInfo) =>
            Schedule(() =>
            {
                var existingSet = replays.FirstOrDefault(b => b.ReplayInfo.Equals(replayInfo));

                if (existingSet == null)
                    return;

                rootCarouselItem.RemoveChild(existingSet);
                itemsCache.Invalidate();
            });

        private CarouselReplay createCarouselReplay(ReplayInfo info)
        {
            var item = new CarouselReplay(info, mapStorage.Get(info.MapId)?.TerrainName ?? "[Missing Map]");

            item.State.ValueChanged += state =>
            {
                if (state.NewValue == CarouselItemState.Selected)
                {
                    selectedReplay = item;
                    SelectionChanged?.Invoke(item.ReplayInfo);
                    itemsCache.Invalidate();
                    ScrollToSelected();
                }
            };

            return item;
        }

        protected override bool OnInvalidate(Invalidation invalidation, InvalidationSource source)
        {
            // handles the vertical size of the carousel changing (ie. on window resize when aspect ratio has changed).
            if ((invalidation & Invalidation.Layout) > 0)
                itemsCache.Invalidate();

            return base.OnInvalidate(invalidation, source);
        }

        public void OnEnter()
        {
            OnSizingChanged();
            searchContainer.FadeInFromZero(300, Easing.OutQuint);
            searchContainer.ScaleTo(new Vector2(1, 0.5f)).ScaleTo(Vector2.One, 500, Easing.OutQuint);
            searchContainer.MoveToY(-40).MoveToY(0, 500, Easing.OutQuint);
        }

        protected override void Update()
        {
            base.Update();
            Scroll.Size = new Vector2(DrawSize.X, DrawSize.Y - header_height);

            bool revalidateItems = !itemsCache.IsValid;

            //First we iterate over all non-filtered carousel items and populate their vertical positions.
            if (revalidateItems)
                updateYPositions();

            if (pendingScrollOperation != PendingScrollOperation.None)
                updateScrollPosition();

            var newDisplayRange = getDisplayRange();

            if (revalidateItems || newDisplayRange != displayedRange)
            {
                displayedRange = newDisplayRange;

                if (visibleItems.Count > 0)
                {
                    var toDisplay = visibleItems.GetRange(displayedRange.first, displayedRange.last - displayedRange.first + 1);

                    foreach (var panel in Scroll.Children)
                    {
                        if (toDisplay.Remove(panel.Item))
                        {
                            if (revalidateItems)
                                panel.MoveToY(panel.Item.CarouselYPosition, 300, Easing.OutCubic);
                            //panel already displayed
                            continue;
                        }

                        panel.ClearTransforms();
                        panel.Expire();
                    }

                    foreach (var item in toDisplay)
                    {
                        var panel = setPool.Get(p => p.Item = item);

                        panel.Depth = item.CarouselYPosition;
                        panel.Y = item.CarouselYPosition;

                        Scroll.Add(panel);
                    }
                }
            }

            // Update externally controlled state of currently visible items (e.g. x-offset and opacity).
            // This is a per-frame update on all drawable panels.
            foreach (DrawableCarouselItem item in Scroll.Children)
                updateItem(item);
        }

        private void updateItem(DrawableCarouselItem item, DrawableCarouselItem parent = null)
        {
            if (!item.IsPresent)
            {
                item.Expire();
                return;
            }

            Vector2 posInScroll = Scroll.ScrollContent.ToLocalSpace(item.Panel.ScreenSpaceDrawQuad.Centre);
            float itemDrawY = posInScroll.Y - visibleUpperBound;
            float dist = Math.Abs(1f - itemDrawY / visibleHalfHeight);

            // adjusting the item's overall X position can cause it to become masked away when
            // child items (difficulties) are still visible.
            //item.Header.X = offsetX(dist, visibleHalfHeight) - (parent?.X ?? 0);
            item.ScaleTo(itemScale(dist));
        }

        private static float itemScale(float dist)
        {
            // The radius of the circle the carousel moves on.
            const float circle_radius = 10;
            float discriminant = MathF.Max(0, circle_radius * circle_radius - dist * dist);
            float x = (circle_radius - MathF.Sqrt(discriminant));
            return 0.95f - x;
        }

        private readonly CarouselBoundsItem carouselBoundsItem = new CarouselBoundsItem();

        private (int firstIndex, int lastIndex) getDisplayRange()
        {
            // Find index range of all items that should be on-screen
            carouselBoundsItem.CarouselYPosition = visibleUpperBound - pixels_offsecreen_before_preloading_replay;
            int firstIndex = visibleItems.BinarySearch(carouselBoundsItem);
            if (firstIndex < 0) firstIndex = ~firstIndex;

            carouselBoundsItem.CarouselYPosition = visibleBottomBound + pixels_offsecreen_before_preloading_replay;
            int lastIndex = visibleItems.BinarySearch(carouselBoundsItem);
            if (lastIndex < 0) lastIndex = ~lastIndex;

            // as we can't be 100% sure on the size of individual carousel drawables,
            // always play it safe and extend bounds by one.
            firstIndex = Math.Max(0, firstIndex - 1);
            lastIndex = Math.Clamp(lastIndex + 1, firstIndex, Math.Max(0, visibleItems.Count - 1));

            return (firstIndex, lastIndex);
        }

        private void updateYPositions()
        {
            visibleItems.Clear();

            float currentY = visibleHalfHeight;

            scrollTarget = null;

            foreach (var item in rootCarouselItem.Children)
            {
                if (item.Filtered.Value)
                    continue;

                switch (item)
                {
                    case CarouselReplay replay:
                    {
                        visibleItems.Add(replay);
                        replay.CarouselYPosition = currentY;

                        if (item.State.Value == CarouselItemState.Selected)
                        {
                            // scroll position at currentY makes the set panel appear at the very top of the carousel's screen space
                            // move down by half of visible height (height of the carousel's visible extent, including semi-transparent areas)
                            // then reapply the top semi-transparent area (because carousel's screen space starts below it)
                            scrollTarget = currentY + DrawableCarouselReplay.SELECTEDHEIGHT - visibleHalfHeight;
                            pendingScrollOperation = PendingScrollOperation.Standard;
                        }

                        currentY += replay.TotalHeight + panel_padding;
                        break;
                    }
                }
            }

            currentY += visibleHalfHeight;
            Scroll.ScrollContent.Height = currentY;
            itemsCache.Validate();

            // update and let external consumers know about selection loss.
            if (ReplaysLoaded)
            {
                bool selectionLost = selectedReplay != null && selectedReplay.State.Value != CarouselItemState.Selected;

                if (selectionLost)
                {
                    selectedReplay = null;
                    SelectionChanged?.Invoke(null);
                }
            }
        }

        private void updateScrollPosition()
        {
            if (scrollTarget != null)
            {
                if (firstScroll)
                {
                    // reduce movement when first displaying the carousel.
                    Scroll.ScrollTo(scrollTarget.Value - 200, false);
                    firstScroll = false;
                }

                switch (pendingScrollOperation)
                {
                    case PendingScrollOperation.Standard:
                        Scroll.ScrollTo(scrollTarget.Value);
                        break;

                    case PendingScrollOperation.Immediate:

                        // in order to simplify animation logic, rather than using the animated version of ScrollTo,
                        // we take the difference in scroll height and apply to all visible panels.
                        // this avoids edge cases like when the visible panels is reduced suddenly, causing ScrollContainer
                        // to enter clamp-special-case mode where it animates completely differently to normal.
                        float scrollChange = scrollTarget.Value - Scroll.Current;
                        Scroll.ScrollTo(scrollTarget.Value, false);
                        foreach (var i in Scroll.Children)
                            i.Y += scrollChange;
                        break;
                }
                pendingScrollOperation = PendingScrollOperation.None;
            }
        }

        public void Select(ReplayInfo info)
        {
            selectedReplay = (CarouselReplay)rootCarouselItem.Children.First(x =>
            {
                var replay = (CarouselReplay)x;

                return replay != null && replay.ReplayInfo.ID == info.ID;
            });

            selectedReplay.State.Value = CarouselItemState.Selected;
            ScrollToSelected(true);
        }

        private void select(CarouselItem item)
        {
            if (!AllowSelection)
                return;

            if (item == null) return;

            item.State.Value = CarouselItemState.Selected;
        }

        private string lastSearchText;
        private CarouselFilter lastSearchFilter;

        private void onSearchTextChange(string text, CarouselFilter filter)
        {
            if (text == lastSearchText && filter == lastSearchFilter)
                return;

            lastSearchText = text;
            lastSearchFilter = filter;

            var splitText = text.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            rootCarouselItem.Filter(splitText, filter);
            itemsCache.Invalidate();
        }

        public void ScrollToSelected(bool immediate = false) => pendingScrollOperation = immediate ? PendingScrollOperation.Immediate : PendingScrollOperation.Standard;

        private enum PendingScrollOperation
        {
            None,
            Standard,
            Immediate,
        }

        /// <summary>
        /// A carousel item strictly used for binary search purposes.
        /// </summary>
        private class CarouselBoundsItem : CarouselItem
        {
            public override DrawableCarouselItem GetDrawableForItem() => throw new NotImplementedException();

            public override void Filter(string[] textParts, CarouselFilter filter) { }
        }

        private class CarouselRoot : EagerSelectCarouselGroup
        {
            private readonly ReplayCarousel carousel;

            public CarouselRoot(ReplayCarousel carousel)
            {
                //This is the base group. It should always be selected.
                State.Value = CarouselItemState.Selected;
                State.ValueChanged += state => State.Value = CarouselItemState.Selected;

                this.carousel = carousel;
            }
        }

        protected class CarouselScrollContainer : UserTrackingScrollContainer<DrawableCarouselItem>
        {
            private bool rightMouseScrollBlocked;

            public CarouselScrollContainer()
            {
                // size is determined by the carousel itself, due to not all content necessarily being loaded.
                ScrollContent.AutoSizeAxes = Axes.None;

                // the scroll container may get pushed off-screen by global screen changes, but we still want panels to display outside of the bounds.
                Masking = false;
            }

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (e.Button == MouseButton.Right)
                {
                    // we need to block right click absolute scrolling when hovering a carousel item so context menus can display.
                    // this can be reconsidered when we have an alternative to right click scrolling.
                    if (GetContainingInputManager().HoveredDrawables.OfType<DrawableCarouselItem>().Any())
                    {
                        rightMouseScrollBlocked = true;
                        return false;
                    }
                }

                rightMouseScrollBlocked = false;
                return base.OnMouseDown(e);
            }

            protected override bool OnDragStart(DragStartEvent e)
            {
                if (rightMouseScrollBlocked)
                    return false;

                return base.OnDragStart(e);
            }
        }

        public class UserTrackingScrollContainer<T> : BasicScrollContainer<T>
            where T : Drawable
        {
            public bool UserScrolling { get; private set; }

            public void CancelUserScroll() => UserScrolling = false;

            public UserTrackingScrollContainer()
            {
            }

            public UserTrackingScrollContainer(Direction direction)
                : base(direction)
            {
            }

            protected override ScrollbarContainer CreateScrollbar(Direction direction)
            {
                var scrollbar = new SizeSettableScrollbar(direction)
                {
                    ScrollbarSize = 18
                };

                scrollbar.Child.Colour = new Color4(50, 100, 50, 255);
                return scrollbar;
            }

            protected override void OnUserScroll(float value, bool animated = true, double? distanceDecay = default)
            {
                UserScrolling = true;
                base.OnUserScroll(value, animated, distanceDecay);
            }

            public new void ScrollIntoView(Drawable target, bool animated = true)
            {
                UserScrolling = false;
                base.ScrollIntoView(target, animated);
            }

            public new void ScrollTo(float value, bool animated = true, double? distanceDecay = null)
            {
                UserScrolling = false;
                base.ScrollTo(value, animated, distanceDecay);
            }

            public new void ScrollToEnd(bool animated = true, bool allowDuringDrag = false)
            {
                UserScrolling = false;
                base.ScrollToEnd(animated, allowDuringDrag);
            }

            protected class SizeSettableScrollbar : BasicScrollbar
            {
                private float scrollbarSize;

                public float ScrollbarSize
                {
                    get => scrollbarSize;
                    set
                    {
                        if (scrollbarSize == value)
                            return;

                        scrollbarSize = value;
                        var val = Size[(int)ScrollDirection];
                        ResizeTo(val);
                    }
                }

                protected override float MinimumDimSize => scrollbarSize;

                public SizeSettableScrollbar(Direction direction)
                    : base(direction)
                {
                }

                public override void ResizeTo(float val, int duration = 0, Easing easing = Easing.None)
                {
                    Vector2 newSize = new Vector2(scrollbarSize);
                    newSize[(int)ScrollDirection] = val;
                    this.ResizeTo<BasicScrollbar>(newSize, (double)duration, easing);
                }
            }
        }
    }
}
