import {describe, expect, it} from 'vitest';

import {InviteLinkUtils} from './invite-link.utils';

describe('InviteLinkUtils.build', () => {
  it('points at the accept-invite page with the user id and token', () => {
    const link = new URL(InviteLinkUtils.build('https://app.example.test', 'user-1', 'abc'));

    expect(link.origin).toBe('https://app.example.test');
    expect(link.pathname).toBe('/accept-invite');
    expect(link.searchParams.get('user')).toBe('user-1');
    expect(link.searchParams.get('token')).toBe('abc');
  });

  it('encodes tokens that contain URL-reserved characters so they round-trip', () => {
    const token = 'CfDJ8+a/b==&x';

    const link = new URL(InviteLinkUtils.build('https://app.example.test', 'user-1', token));

    expect(link.search).not.toContain('+a/b==&x');
    expect(link.searchParams.get('token')).toBe(token);
  });
});
