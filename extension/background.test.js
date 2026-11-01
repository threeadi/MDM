import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
// @ts-expect-error CommonJS import in ESM
import bg from './background.js';
const {
  DEFAULT_REGISTERED_EXTENSIONS,
  getFileExtension,
  isExtensionRegistered,
  closeMatchingNewTab,
  extractFilenameFromUrl,
  newlyOpenedTabs
} = bg;

describe('getFileExtension', () => {
  it('correctly extracts file extensions from URLs and filenames', () => {
    expect(getFileExtension('https://example.com/file.zip')).toBe('zip');
    expect(getFileExtension('https://example.com/path/archive.tar.gz')).toBe('gz');
    expect(getFileExtension('https://cdn.site.com/video.MP4?token=xyz#frag')).toBe('mp4');
    expect(getFileExtension('document.PDF')).toBe('pdf');
    expect(getFileExtension('installer.msi')).toBe('msi');
    expect(getFileExtension('disc.iso')).toBe('iso');
  });

  it('handles query strings, hashes, and URLs without extensions', () => {
    expect(getFileExtension('https://example.com/download')).toBe('');
    expect(getFileExtension('https://example.com/api/v1/stream?format=raw')).toBe('');
    expect(getFileExtension('')).toBe('');
    expect(getFileExtension(null)).toBe('');
  });
});

describe('isExtensionRegistered', () => {
  it('identifies registered file types correctly', () => {
    expect(isExtensionRegistered('file.zip')).toBe(true);
    expect(isExtensionRegistered('https://example.com/setup.exe')).toBe(true);
    expect(isExtensionRegistered('https://example.com/image.iso')).toBe(true);
    expect(isExtensionRegistered('song.mp3')).toBe(true);
    expect(isExtensionRegistered('paper.pdf')).toBe(true);
  });

  it('returns false for non-registered extensions when whitelist is active', () => {
    expect(isExtensionRegistered('script.js')).toBe(false);
    expect(isExtensionRegistered('styles.css')).toBe(false);
    expect(isExtensionRegistered('page.html')).toBe(false);
    expect(isExtensionRegistered('image.png')).toBe(false);
  });

  it('returns true for URLs without clear extension so backend prober can inspect it', () => {
    expect(isExtensionRegistered('https://example.com/download-item')).toBe(true);
  });

  it('allows custom whitelist', () => {
    const customList = ['mkv', 'flac'];
    expect(isExtensionRegistered('movie.mkv', customList)).toBe(true);
    expect(isExtensionRegistered('setup.exe', customList)).toBe(false);
  });
});

describe('closeMatchingNewTab', () => {
  const originalChrome = globalThis.chrome;

  afterEach(() => {
    globalThis.chrome = originalChrome;
    newlyOpenedTabs.clear();
    vi.restoreAllMocks();
  });

  it('closes tab if URL matches download URL within threshold', () => {
    const mockRemove = vi.fn();
    globalThis.chrome = {
      tabs: {
        remove: mockRemove
      }
    };

    newlyOpenedTabs.set(101, {
      url: 'https://example.com/file.zip',
      createdAt: Date.now()
    });

    closeMatchingNewTab('https://example.com/file.zip', 'https://example.com/page');
    expect(mockRemove).toHaveBeenCalledWith(101);
    expect(newlyOpenedTabs.has(101)).toBe(false);
  });

  it('closes tab if newly opened tab was about:blank', () => {
    const mockRemove = vi.fn();
    globalThis.chrome = {
      tabs: {
        remove: mockRemove
      }
    };

    newlyOpenedTabs.set(102, {
      url: 'about:blank',
      createdAt: Date.now()
    });

    closeMatchingNewTab('https://cdn.example.com/data.rar', 'https://example.com');
    expect(mockRemove).toHaveBeenCalledWith(102);
    expect(newlyOpenedTabs.has(102)).toBe(false);
  });

  it('ignores tabs that are older than 15 seconds', () => {
    const mockRemove = vi.fn();
    globalThis.chrome = {
      tabs: {
        remove: mockRemove
      }
    };

    newlyOpenedTabs.set(103, {
      url: 'https://example.com/file.zip',
      createdAt: Date.now() - 20000 // 20s ago
    });

    closeMatchingNewTab('https://example.com/file.zip', '');
    expect(mockRemove).not.toHaveBeenCalled();
  });
});

