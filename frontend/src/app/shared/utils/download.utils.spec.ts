import {DownloadUtils} from './download.utils';

describe('DownloadUtils.exportFileName', () => {
  it('names the file for the UTC day', () => {
    expect(DownloadUtils.exportFileName(new Date('2026-11-03T23:59:59Z'))).toBe(
      'finance-sentry-export-2026-11-03.json'
    );
  });
});

describe('DownloadUtils.saveBlob', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('clicks a link to the blob under the given name, then releases the object URL', () => {
    const create = vi.fn().mockReturnValue('blob:export');
    const revoke = vi.fn();
    vi.stubGlobal('URL', {createObjectURL: create, revokeObjectURL: revoke});
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined);
    const anchors: HTMLAnchorElement[] = [];
    const createElement = document.createElement.bind(document);
    vi.spyOn(document, 'createElement').mockImplementation((tag: string) => {
      const element = createElement(tag);
      if (element instanceof HTMLAnchorElement) {
        anchors.push(element);
      }
      return element;
    });
    const blob = new Blob(['{}'], {type: 'application/json'});

    DownloadUtils.saveBlob(blob, 'export.json');

    expect(create).toHaveBeenCalledWith(blob);
    expect(anchors).toHaveLength(1);
    expect(anchors[0].download).toBe('export.json');
    expect(anchors[0].href).toContain('blob:export');
    expect(click).toHaveBeenCalledTimes(1);
    expect(revoke).toHaveBeenCalledWith('blob:export');
    vi.unstubAllGlobals();
  });
});
