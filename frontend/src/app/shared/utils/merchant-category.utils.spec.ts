import {MerchantCategoryUtils} from './merchant-category.utils';

describe('MerchantCategoryUtils', () => {
  describe('format', () => {
    it('title-cases a single-word category', () => {
      expect(MerchantCategoryUtils.format('GROCERIES')).toBe('Groceries');
    });

    it('splits underscores into spaced words', () => {
      expect(MerchantCategoryUtils.format('EATING_OUT')).toBe('Eating Out');
    });

    it('turns _AND_ into an ampersand', () => {
      expect(MerchantCategoryUtils.format('HEALTH_AND_BEAUTY')).toBe('Health & beauty');
    });

    it('returns an empty string for an empty category', () => {
      expect(MerchantCategoryUtils.format('')).toBe('');
    });
  });
});
