import {type CareerEvent} from '../../models/career-event/career-event.model';

export const FUTURE_RAISE: CareerEvent = {
  id: 'e-1',
  kind: 'IncomeStep',
  date: '2027-03-01',
  endDate: null,
  label: 'Promotion',
  amount: 500,
  currency: 'EUR',
  note: null,
  counted: true,
  retired: false,
  updatedAt: '2026-10-01T10:00:00Z',
};

export const FUTURE_LEAVE: CareerEvent = {
  id: 'e-2',
  kind: 'Leave',
  date: '2027-06-01',
  endDate: '2027-08-31',
  label: 'Sabbatical',
  amount: 1200,
  currency: 'USD',
  note: 'Unpaid',
  counted: true,
  retired: false,
  updatedAt: '2026-10-02T10:00:00Z',
};

export const PAST_BONUS: CareerEvent = {
  id: 'e-3',
  kind: 'OneOffIn',
  date: '2026-01-15',
  endDate: null,
  label: 'Year-end bonus',
  amount: 2000,
  currency: 'USD',
  note: null,
  counted: false,
  retired: false,
  updatedAt: '2026-01-16T10:00:00Z',
};

export const RETIRED_COST: CareerEvent = {
  id: 'e-4',
  kind: 'OneOffOut',
  date: '2027-09-01',
  endDate: null,
  label: 'Course fees',
  amount: 900,
  currency: 'USD',
  note: null,
  counted: false,
  retired: true,
  updatedAt: '2026-10-03T10:00:00Z',
};
