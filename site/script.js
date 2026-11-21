(() => {
  'use strict';

  /* ------------------------------------------------------------ header -- */

  const header = document.querySelector('.site-header');
  const onScroll = () => {
    if (!header) return;
    header.classList.toggle('scrolled', window.scrollY > 8);
  };
  onScroll();
  window.addEventListener('scroll', onScroll, { passive: true });

  /* --------------------------------------------------------- mobile nav -- */

  const navToggle = document.querySelector('.nav-toggle');
  const nav = document.getElementById('menu');

  const setNav = (open) => {
    if (!nav || !navToggle) return;
    nav.classList.toggle('open', open);
    navToggle.setAttribute('aria-expanded', String(open));
    navToggle.setAttribute('aria-label', open ? 'Fechar menu' : 'Abrir menu');
  };

  if (navToggle && nav) {
    navToggle.addEventListener('click', () => {
      setNav(!nav.classList.contains('open'));
    });

    nav.addEventListener('click', (event) => {
      if (event.target.closest('a')) setNav(false);
    });

    document.addEventListener('click', (event) => {
      if (!nav.classList.contains('open')) return;
      if (event.target.closest('.nav') || event.target.closest('.nav-toggle')) return;
      setNav(false);
    });

    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape' && nav.classList.contains('open')) {
        setNav(false);
        navToggle.focus();
      }
    });

    window.addEventListener('resize', () => {
      if (window.innerWidth > 900 && nav.classList.contains('open')) setNav(false);
    });
  }

  /* -------------------------------------------------------- scroll reveal -- */

  const reveals = Array.from(document.querySelectorAll('.reveal'));
  if (reveals.length) {
    const revealAll = () => reveals.forEach((element) => element.classList.add('visible'));

    if ('IntersectionObserver' in window) {
      document.body.classList.add('reveal-ready');

      const observer = new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) return;
          entry.target.classList.add('visible');
          observer.unobserve(entry.target);
        });
      }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });

      reveals.forEach((element) => observer.observe(element));

      /* Rede de segurança: se o observer não disparar (capturas headless,
         pré-renderizadores, engine antiga), nada fica invisível. */
      const safety = window.setTimeout(() => {
        reveals.forEach((element) => {
          if (element.classList.contains('visible')) return;
          element.classList.add('visible');
          observer.unobserve(element);
        });
      }, 1500);

      window.addEventListener('pagehide', () => window.clearTimeout(safety), { once: true });
    } else {
      revealAll();
    }
  }

  /* ------------------------------------------------------------ lightbox -- */

  const lightbox = document.getElementById('lightbox');
  const lightboxImg = document.getElementById('lightbox-img');
  const lightboxTitle = document.getElementById('lightbox-title');
  const lightboxCaption = document.getElementById('lightbox-caption');
  const lightboxClose = lightbox ? lightbox.querySelector('.lightbox-close') : null;
  let lastTrigger = null;

  const openLightbox = (trigger) => {
    if (!lightbox || !lightboxImg) return;
    lastTrigger = trigger;

    lightboxImg.src = trigger.dataset.full || '';
    lightboxImg.alt = trigger.querySelector('img')?.alt || '';
    lightboxTitle.textContent = trigger.dataset.title || '';
    lightboxCaption.textContent = trigger.dataset.caption || '';

    lightbox.classList.add('open');
    lightbox.setAttribute('aria-hidden', 'false');
    document.body.style.overflow = 'hidden';
    lightboxClose?.focus();
  };

  const closeLightbox = () => {
    if (!lightbox || !lightbox.classList.contains('open')) return;
    lightbox.classList.remove('open');
    lightbox.setAttribute('aria-hidden', 'true');
    document.body.style.overflow = '';
    lightboxImg.src = '';
    lastTrigger?.focus();
    lastTrigger = null;
  };

  if (lightbox) {
    document.querySelectorAll('.shot').forEach((trigger) => {
      trigger.addEventListener('click', () => openLightbox(trigger));
    });

    lightboxClose?.addEventListener('click', closeLightbox);

    lightbox.addEventListener('click', (event) => {
      if (event.target === lightbox) closeLightbox();
    });

    document.addEventListener('keydown', (event) => {
      if (!lightbox.classList.contains('open')) return;

      if (event.key === 'Escape') {
        closeLightbox();
        return;
      }

      if (event.key === 'Tab') {
        event.preventDefault();
        lightboxClose?.focus();
      }
    });
  }

  /* ----------------------------------------------------------- copy hash -- */

  const copyText = async (text) => {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
      return;
    }

    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    document.execCommand('copy');
    document.body.removeChild(area);
  };

  document.querySelectorAll('[data-copy]').forEach((button) => {
    button.addEventListener('click', async () => {
      const original = button.textContent;
      try {
        await copyText(button.dataset.copy || '');
        button.textContent = 'Copiado';
        button.classList.add('copied');
      } catch {
        button.textContent = 'Falhou';
      }

      window.setTimeout(() => {
        button.textContent = original;
        button.classList.remove('copied');
      }, 2200);
    });
  });

  /* -------------------------------------------------------- netlify badge -- */

  // O edge do Netlify injeta o selo num iframe fixo no fim do body. O host do
  // iframe está no nosso documento, então escondemos o elemento e o mantemos
  // escondido caso o script recrie ou reexiba a moldura.
  const hideNetlifyBadge = () => {
    const frame = document.getElementById('nl-badge-frame');
    if (!frame) return;

    frame.style.setProperty('display', 'none', 'important');
    frame.style.setProperty('visibility', 'hidden', 'important');
    frame.setAttribute('aria-hidden', 'true');
    frame.setAttribute('tabindex', '-1');
  };

  hideNetlifyBadge();
  new MutationObserver(hideNetlifyBadge).observe(document.documentElement, {
    childList: true,
    subtree: true,
  });
})();
