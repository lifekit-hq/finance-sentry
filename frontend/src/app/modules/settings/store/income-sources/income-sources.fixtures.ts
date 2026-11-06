import {type IncomeSource} from '../../models/income-source/income-source.model';

export const MONTHLY_SALARY: IncomeSource = {
  id: 's-1',
  kind: 'Salary',
  label: 'Day job',
  amount: 3000,
  currency: 'EUR',
  cadence: 'Monthly',
  grossOrNet: 'Net',
  startDate: '2026-01-01',
  endDate: null,
  monthlyEquivalent: 3000,
  active: true,
  updatedAt: '2026-10-01T10:00:00Z',
};

export const RETIRED_SIDE: IncomeSource = {
  id: 's-2',
  kind: 'Side',
  label: 'Weekend gig',
  amount: 100,
  currency: 'USD',
  cadence: 'Irregular',
  grossOrNet: 'Gross',
  startDate: '2026-02-01',
  endDate: '2026-06-30',
  monthlyEquivalent: null,
  active: false,
  updatedAt: '2026-07-01T10:00:00Z',
};
