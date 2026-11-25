// Holds a Web Lock across .NET code: acquire() resolves once this tab has the lock, with an object
// whose release() gives it up. See Services/Auth/IRefreshLock.cs.
window.pingoRefreshLock = {
    acquire: () => new Promise((acquired, failed) => {
        // Only secure pages (HTTPS or localhost) have Web Locks; elsewhere tabs don't take turns.
        if (!navigator.locks) {
            acquired({ release: () => { } });
            return;
        }

        // The lock is held until the promise returned by this callback resolves.
        navigator.locks
            .request("pingo.refresh", () => new Promise(release => acquired({ release: () => release() })))
            .catch(failed);
    })
};
