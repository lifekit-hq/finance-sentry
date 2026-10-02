import {MarkdownUtils} from './markdown.utils';

describe('MarkdownUtils', () => {
  it('renders **bold** as strong with no literal asterisks', () => {
    const html = MarkdownUtils.toSafeHtml('Up **31%** this year');
    expect(html).toBe('Up <strong>31%</strong> this year');
    expect(html).not.toContain('*');
  });

  it('renders *italic* and newlines', () => {
    expect(MarkdownUtils.toSafeHtml('*calm*\nmarkets')).toBe('<em>calm</em><br>markets');
  });

  it('escapes unsafe HTML', () => {
    const html = MarkdownUtils.toSafeHtml('<script>alert(1)</script><img src=x onerror="x()">');
    expect(html).not.toContain('<script');
    expect(html).not.toContain('<img');
    expect(html).toContain('&lt;script&gt;');
  });

  it('keeps unsafe HTML escaped inside emphasis', () => {
    expect(MarkdownUtils.toSafeHtml('**<b onclick=1>x</b>**')).toBe(
      '<strong>&lt;b onclick=1&gt;x&lt;/b&gt;</strong>'
    );
  });
});
