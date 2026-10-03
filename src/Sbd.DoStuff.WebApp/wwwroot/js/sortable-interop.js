// Wires SortableJS onto the editor's category containers. Blazor owns the DOM, so a drop is reverted
// here and reported to .NET, which updates the model and re-renders the rows in their new position.
export function init(root, dotNetRef) {
    root.querySelectorAll('[data-sortable]').forEach(container => {
        if (container._sortable) {
            return;
        }

        container._sortable = Sortable.create(container, {
            group: 'entries',
            handle: '.drag-handle',
            animation: 150,
            ghostClass: 'opacity-40',
            onEnd(evt) {
                const { item, from, to, oldIndex, newIndex } = evt;
                item.remove();
                from.insertBefore(item, from.children[oldIndex] ?? null);

                if (from === to && oldIndex === newIndex) {
                    return;
                }

                dotNetRef.invokeMethodAsync('OnReorder', item.dataset.key, from.dataset.path, to.dataset.path, newIndex);
            },
        });
    });
}

export function dispose(root) {
    root.querySelectorAll('[data-sortable]').forEach(container => {
        container._sortable?.destroy();
        container._sortable = null;
    });
}
