// The browser build's bridge to the page (Assets/WebGLTemplates/BorrowedSeconds/index.html): the on-screen touch
// controls the page draws over the game (window.bsTouch), and an audio unlock iOS accepts. Game.WebBridge calls these.
mergeInto(LibraryManager.library, {
  // Starts the bridge; returns the page's flags (BSWeb_Flags).
  BSWeb_Init: function () {
    if (!window.__bsAudioUnlock) {
      window.__bsAudioUnlock = true;
      // Unity resumes its AudioContext on touchstart, which iOS doesn't count as a gesture: sound may only start
      // from a touchend (or a click or key press), so resume it there too
      var resume = function () {
        try {
          var ctx = WEBAudio && WEBAudio.audioContext;
          if (ctx && ctx.state !== "running" && ctx.state !== "closed") ctx.resume().catch(function () {});
        } catch (e) { }
      };
      window.addEventListener("touchend", resume, true);
      window.addEventListener("pointerup", resume, true);
      window.addEventListener("click", resume, true);
      window.addEventListener("keydown", resume, true);
    }
    return window.bsTouch ? window.bsTouch.flags() : 0;
  },

  // bit 0: a touch-first device (coarse pointer, no fine one); bit 1: a phone-sized screen; bit 2: the last session
  // in this tab ended without the page closing (the browser most likely killed it for memory)
  BSWeb_Flags: function () {
    return window.bsTouch ? window.bsTouch.flags() : 0;
  },

  // 1 while the on-screen controls are in use (shown), 0 after a key, mouse or pad took over
  BSWeb_TouchActive: function () {
    return window.bsTouch && window.bsTouch.active ? 1 : 0;
  },

  // the on-screen buttons held now, one bit each (Game.WebBridge.Button)
  BSWeb_Held: function () {
    return window.bsTouch ? window.bsTouch.held() : 0;
  },

  // the buttons pressed since the last call, so a tap shorter than a frame still counts
  BSWeb_Pressed: function () {
    return window.bsTouch ? window.bsTouch.takePresses() : 0;
  },

  // a tap on the game since the last call, in canvas pixels from the bottom left: x * 65536 + y, or -1
  BSWeb_TakeTap: function () {
    return window.bsTouch ? window.bsTouch.takeTap() : -1;
  },

  // which controls to show: 0 none, 1 menus, 2 the title (no Back), 3 a level, 4 watching a solution, 5 the level select
  BSWeb_SetMode: function (mode) {
    if (window.bsTouch) window.bsTouch.setMode(mode);
  },

  // a key, the mouse or a pad was used in the game: hide the on-screen controls until the next touch
  BSWeb_OtherInput: function () {
    if (window.bsTouch) window.bsTouch.deactivate();
  },

  // the controls' corners as fractions of the page: 0, 1 the d-pad's width and height from the bottom left; 2, 3 the
  // buttons' from the bottom right; 4, 5 the pause and hint buttons' from the top right (with the clock above them)
  BSWeb_Zone: function (i) {
    return window.bsTouch ? window.bsTouch.zone(i) : 0;
  },
});
