// Wires SortableJS onto the organizer: task lists (move tasks between categories) and category lists
// (reorder / re-parent categories). Blazor owns the DOM, so each drop is reverted here and reported to
// .NET, which updates the model and re-renders.
// forceFallback drives the drag from mouse events instead of native HTML5 drag-and-drop, which only
// auto-scrolls unreliably; with it the page scrolls when an item is held near the top or bottom edge.
const scrolling = {
    forceFallback: true,
    fallbackOnBody: true,
    scroll: true,
    bubbleScroll: true,
    scrollSensitivity: 120,
    scrollSpeed: 20,
};

// Animation is off on purpose: with it, the neighbour keeps visually sitting under the pointer for a moment
// after a swap and gets swapped straight back, so an item could never be dropped at the top of a list.

// When an item first enters another list, SortableJS picks before/after from its own bookkeeping, which
// often lands one place too low (so nothing can be dropped at the very top). Decide from the pointer
// instead: upper half of the hovered item inserts before it, lower half after.
function onMove(evt, original) {
    if (evt.dragged.parentNode === evt.to || !original || original.clientY === undefined) {
        return undefined;
    }

    const rect = evt.relatedRect;
    return original.clientY < rect.top + rect.height / 2 ? -1 : 1;
}

function revert(evt) {
    const { item, from, oldIndex } = evt;
    item.remove();
    from.insertBefore(item, from.children[oldIndex] ?? null);
}

export function init(root, dotNetRef) {
    root.querySelectorAll('[data-task-list]').forEach(container => {
        if (container._sortable) {
            return;
        }

        container._sortable = Sortable.create(container, {
            ...scrolling,
            group: 'organizer-tasks',
            handle: '.task-handle',
            animation: 0,
            ghostClass: 'opacity-40',
            onMove,
            onEnd(evt) {
                revert(evt);
                const { item, from, to, oldIndex, newIndex } = evt;
                if (from === to && oldIndex === newIndex) {
                    return;
                }

                dotNetRef.invokeMethodAsync('OnMoveTask', item.dataset.key, from.dataset.node, to.dataset.node, newIndex);
            },
        });
    });

    root.querySelectorAll('[data-cat-list]').forEach(container => {
        if (container._sortable) {
            return;
        }

        container._sortable = Sortable.create(container, {
            ...scrolling,
            group: 'organizer-categories',
            handle: '.cat-handle',
            animation: 0,
            swapThreshold: 0.65,
            ghostClass: 'opacity-40',
            onMove,
            onStart() {
                root.classList.add('is-dragging-category');
            },
            onEnd(evt) {
                root.classList.remove('is-dragging-category');
                revert(evt);
                const { item, from, to, oldIndex, newIndex } = evt;
                if (from === to && oldIndex === newIndex) {
                    return;
                }

                dotNetRef.invokeMethodAsync('OnMoveCategory', item.dataset.id, to.dataset.parent, newIndex);
            },
        });
    });
}

export function dispose(root) {
    root.querySelectorAll('[data-task-list], [data-cat-list]').forEach(container => {
        container._sortable?.destroy();
        container._sortable = null;
    });
}
