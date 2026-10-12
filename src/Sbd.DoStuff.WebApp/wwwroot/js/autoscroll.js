// Keeps a scrollable output pane pinned to the bottom as lines are appended, until the user scrolls
// up. Scrolling back to the bottom resumes following. Blazor patches the DOM, so a MutationObserver
// sees each appended line without the component needing to call back after every render.
const threshold = 8;

export function attach(el) {
    if (el._autoscroll) {
        return;
    }

    let following = true;

    // Content growth doesn't fire a scroll event while we're pinned, so a scroll event that leaves
    // the pane away from the bottom can only be the user scrolling up.
    const onScroll = () => {
        following = el.scrollHeight - el.scrollTop - el.clientHeight <= threshold;
    };

    const observer = new MutationObserver(() => {
        if (following) {
            el.scrollTop = el.scrollHeight;
        }
    });

    el.addEventListener('scroll', onScroll, { passive: true });
    observer.observe(el, { childList: true, subtree: true, characterData: true });
    el.scrollTop = el.scrollHeight;

    el._autoscroll = { observer, onScroll };
}

export function detach(el) {
    const state = el?._autoscroll;
    if (!state) {
        return;
    }

    state.observer.disconnect();
    el.removeEventListener('scroll', state.onScroll);
    el._autoscroll = null;
}
